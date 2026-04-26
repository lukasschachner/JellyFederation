#!/usr/bin/env bash
# JellyFederation dev environment manager
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="/tmp/jellyfin-dev"
CONTAINER="jellyfin-dev"
PORT=8097
PLUGIN_ID="e5c0cda1-805e-41e2-9654-e17143dc31a1"
PLUGIN_VERSION="$(awk -F'"' '/^version:/{print $2; exit}' "$REPO_DIR/build.yaml")"
PLUGIN_DIR="$DATA_DIR/config/plugins/JellyFederation_${PLUGIN_VERSION}"
SERVER_PID_FILE="/tmp/jf-server.pid"
SERVER_LOG="/tmp/jf-server.log"
FEDERATION_PORT=5264
COMPOSE_FILE="$REPO_DIR/docker-compose.yml"
TEST_HOST_DEFAULT="192.168.2.192"
TEST_USER_DEFAULT="root"
TEST_PLUGIN_DIR="/opt/media/jellyfin/plugins/JellyFederation_${PLUGIN_VERSION}"
TEST_COMPOSE_DIR="/opt/media/jellyfin"

# ── colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'

info()    { echo -e "${CYAN}▶${RESET} $*"; }
ok()      { echo -e "${GREEN}✓${RESET} $*"; }
warn()    { echo -e "${YELLOW}⚠${RESET} $*"; }
err()     { echo -e "${RED}✗${RESET} $*" >&2; }
heading() { echo -e "\n${BOLD}$*${RESET}"; }

# ── helpers ───────────────────────────────────────────────────────────────────
ensure_msquic_runtime_asset() {
    local host_lib=""
    local candidates=(
        "/usr/lib/x86_64-linux-gnu/libmsquic.so"
        "/usr/lib/x86_64-linux-gnu/libmsquic.so.2"
        "/usr/lib64/libmsquic.so"
        "/usr/lib/libmsquic.so"
    )
    for c in "${candidates[@]}"; do
        if [[ -f "$c" ]]; then
            host_lib="$c"
            break
        fi
    done

    if [[ -z "$host_lib" ]]; then
        info "Installing host libmsquic (OpenSSL 3 build)…"
        sudo apt-get update -qq
        sudo apt-get install -y -qq libmsquic
        for c in "${candidates[@]}"; do
            if [[ -f "$c" ]]; then
                host_lib="$c"
                break
            fi
        done
    fi

    if [[ -z "$host_lib" ]]; then
        err "Could not locate libmsquic.so on host after installation attempt"
        exit 1
    fi

    mkdir -p /tmp/jf-plugin/runtimes/linux-x64/native
    cp -L "$host_lib" /tmp/jf-plugin/runtimes/linux-x64/native/libmsquic.so
    ok "Bundled host libmsquic → /tmp/jf-plugin/runtimes/linux-x64/native/libmsquic.so"
}

ensure_local_container_msquic() {
    local src="/config/plugins/JellyFederation_${PLUGIN_VERSION}/runtimes/linux-x64/native/libmsquic.so"
    local lib_dir="/usr/lib/x86_64-linux-gnu"

    if ! container_running; then
        return
    fi

    docker exec "$CONTAINER" sh -lc "
set -e
if [ ! -f '$src' ]; then
  echo 'missing plugin-bundled libmsquic at $src' >&2
  exit 1
fi
mkdir -p '$lib_dir'
cp -f '$src' '$lib_dir/libmsquic.so.2'
ln -sf '$lib_dir/libmsquic.so.2' '$lib_dir/libmsquic.so'
ldconfig
" >/dev/null

    ok "Ensured local container libmsquic is available via dynamic linker"
}

build_plugin() {
    info "Building plugin…"
    rm -rf /tmp/jf-plugin
    dotnet publish "$REPO_DIR/src/JellyFederation.Plugin" -c Release -r linux-x64 --self-contained false -o /tmp/jf-plugin -v q 2>&1 \
        | grep -v "^$" | grep -v "^Build" | grep -v "Determining" | grep -v "All projects" || true
    ensure_msquic_runtime_asset
    ok "Plugin built → /tmp/jf-plugin"
}

build_frontend() {
    info "Building frontend…"
    (cd "$REPO_DIR/src/JellyFederation.Web" && npm run build --silent 2>&1 | tail -3)
    mkdir -p "$REPO_DIR/src/JellyFederation.Server/wwwroot"
    cp -r "$REPO_DIR/src/JellyFederation.Web/dist/." "$REPO_DIR/src/JellyFederation.Server/wwwroot/"
    ok "Frontend built → wwwroot"
}

install_plugin() {
    if ! mkdir -p "$PLUGIN_DIR" 2>/dev/null; then
        sudo mkdir -p "$PLUGIN_DIR"
    fi

    if [[ -w "$PLUGIN_DIR" ]]; then
        rm -rf "$PLUGIN_DIR"/* 2>/dev/null || true
        cp -a /tmp/jf-plugin/. "$PLUGIN_DIR/"
        cat > "$PLUGIN_DIR/meta.json" << EOF
{"Id":"$PLUGIN_ID","Name":"JellyFederation","Version":"$PLUGIN_VERSION","Status":"Active"}
EOF
    else
        sudo rm -rf "$PLUGIN_DIR"/* 2>/dev/null || true
        sudo cp -a /tmp/jf-plugin/. "$PLUGIN_DIR/"
        printf '{"Id":"%s","Name":"JellyFederation","Version":"%s","Status":"Active"}\n' "$PLUGIN_ID" "$PLUGIN_VERSION" \
            | sudo tee "$PLUGIN_DIR/meta.json" >/dev/null
    fi

    ok "Plugin installed → $PLUGIN_DIR"
}

reset_plugin_status() {
    if [[ -f "$PLUGIN_DIR/meta.json" ]]; then
        if [[ -w "$PLUGIN_DIR/meta.json" ]]; then
            python3 -c "
import json, pathlib
p = pathlib.Path('$PLUGIN_DIR/meta.json')
d = json.loads(p.read_text())
d['Status'] = 'Active'
p.write_text(json.dumps(d))
"
        else
            sudo python3 -c "
import json, pathlib
p = pathlib.Path('$PLUGIN_DIR/meta.json')
d = json.loads(p.read_text())
d['Status'] = 'Active'
p.write_text(json.dumps(d))
"
        fi
    fi
}

container_running() {
    docker ps --format '{{.Names}}' 2>/dev/null | grep -q "^${CONTAINER}$"
}

container_exists() {
    docker ps -a --format '{{.Names}}' 2>/dev/null | grep -q "^${CONTAINER}$"
}

server_running() {
    [[ -f "$SERVER_PID_FILE" ]] && kill -0 "$(cat "$SERVER_PID_FILE")" 2>/dev/null
}

wait_healthy() {
    local url="$1" label="$2" retries=20
    info "Waiting for $label…"
    for ((i=1; i<=retries; i++)); do
        if curl -sf "$url" >/dev/null 2>&1; then
            ok "$label is up"
            return 0
        fi
        sleep 1
    done
    warn "$label did not respond after ${retries}s"
    return 1
}

compose() {
    docker compose -f "$COMPOSE_FILE" "$@"
}

host_lan_ip() {
    ip route get 1.1.1.1 2>/dev/null | awk '{print $7; exit}' || hostname -I | awk '{print $1}'
}

db_get_server() {
    local db="$1"
    local preferred_name="${2:-}"
    local exclude_id="${3:-}"
    python3 - "$db" "$preferred_name" "$exclude_id" << 'PYEOF'
import sqlite3, sys
db, preferred, exclude = sys.argv[1], sys.argv[2], sys.argv[3]
conn = sqlite3.connect(db)
row = None
try:
    tables = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Servers' LIMIT 1"
    ).fetchone()
    if not tables:
        conn.close()
        raise SystemExit(0)

    if preferred:
        if exclude:
            row = conn.execute(
                "SELECT Id, ApiKey, Name FROM Servers WHERE lower(Name)=lower(?) AND Id != ? ORDER BY RegisteredAt DESC LIMIT 1",
                (preferred, exclude),
            ).fetchone()
        else:
            row = conn.execute(
                "SELECT Id, ApiKey, Name FROM Servers WHERE lower(Name)=lower(?) ORDER BY RegisteredAt DESC LIMIT 1",
                (preferred,),
            ).fetchone()

    if row is None:
        if exclude:
            row = conn.execute(
                "SELECT Id, ApiKey, Name FROM Servers WHERE Id != ? ORDER BY RegisteredAt DESC LIMIT 1",
                (exclude,),
            ).fetchone()
        else:
            row = conn.execute(
                "SELECT Id, ApiKey, Name FROM Servers ORDER BY RegisteredAt DESC LIMIT 1"
            ).fetchone()
except sqlite3.OperationalError:
    conn.close()
    raise SystemExit(0)

if row:
    print(row[0], row[1], row[2])
conn.close()
PYEOF
}

ensure_local_container_running() {
    if container_exists; then
        if ! container_running; then
            reset_plugin_status
            info "Starting Jellyfin container…"
            docker start "$CONTAINER" >/dev/null
            ensure_local_container_msquic
            ok "Jellyfin started"
        fi
        return
    fi

    if ! mkdir -p "$DATA_DIR"/{config,cache,media/movies,media/series,media/music} 2>/dev/null; then
        sudo mkdir -p "$DATA_DIR"/{config,cache,media/movies,media/series,media/music}
    fi
    info "Starting Jellyfin on port $PORT…"
    docker run -d \
        --name "$CONTAINER" \
        -p "${PORT}:8096" \
        -p "7777:7777/udp" \
        --cap-add SYS_ADMIN \
        --cap-add SYS_NICE \
        --add-host=host.docker.internal:host-gateway \
        -v "$DATA_DIR/config:/config" \
        -v "$DATA_DIR/cache:/cache" \
        -v "$DATA_DIR/media:/media" \
        jellyfin/jellyfin >/dev/null
    ensure_local_container_msquic
    ok "Jellyfin container started"
}

deploy_local_from_tmp() {
    install_plugin
    reset_plugin_status
    ensure_local_container_running
    info "Restarting local Jellyfin to pick up plugin…"
    docker restart "$CONTAINER" >/dev/null
    ensure_local_container_msquic
    ok "Local Jellyfin restarted"
}

deploy_remote_from_tmp() {
    local test_host="${1:-$TEST_HOST_DEFAULT}"
    local test_user="${2:-$TEST_USER_DEFAULT}"
    info "Deploying plugin artifacts to ${test_user}@${test_host}:${TEST_PLUGIN_DIR}…"
    ssh "${test_user}@${test_host}" "mkdir -p '${TEST_PLUGIN_DIR}'"
    ssh "${test_user}@${test_host}" "rm -rf '${TEST_PLUGIN_DIR}'/* 2>/dev/null || true"
    tar -C /tmp/jf-plugin -cf - . | ssh "${test_user}@${test_host}" "tar -C '${TEST_PLUGIN_DIR}' -xf -"
    ssh "${test_user}@${test_host}" "printf '{\"Id\":\"%s\",\"Name\":\"JellyFederation\",\"Version\":\"%s\",\"Status\":\"Active\"}\n' '${PLUGIN_ID}' '${PLUGIN_VERSION}' > '${TEST_PLUGIN_DIR}/meta.json'"
    ok "Plugin artifacts deployed to $test_host"
    info "Restarting remote Jellyfin on $test_host…"
    ssh "${test_user}@${test_host}" "cd '${TEST_COMPOSE_DIR}' && docker compose restart jellyfin"
    ok "Remote Jellyfin restarted on $test_host"
}

# ── commands ──────────────────────────────────────────────────────────────────
cmd_setup() {
    heading "Setting up JellyFederation dev environment"

    # Build plugin
    build_plugin
    install_plugin

    # Create data dirs
    if ! mkdir -p "$DATA_DIR"/{config,cache,media/movies,media/series,media/music} 2>/dev/null; then
        sudo mkdir -p "$DATA_DIR"/{config,cache,media/movies,media/series,media/music}
    fi
    ok "Data dirs created under $DATA_DIR"

    # Pull and start Jellyfin
    if container_exists; then
        warn "Container '$CONTAINER' already exists — use '$0 reset' to start fresh"
    else
        info "Starting Jellyfin on port $PORT…"
        docker run -d \
            --name "$CONTAINER" \
            -p "${PORT}:8096" \
            -p "7777:7777/udp" \
            --cap-add SYS_ADMIN \
            --cap-add SYS_NICE \
            --add-host=host.docker.internal:host-gateway \
            -v "$DATA_DIR/config:/config" \
            -v "$DATA_DIR/cache:/cache" \
            -v "$DATA_DIR/media:/media" \
            jellyfin/jellyfin >/dev/null
        ensure_local_container_msquic
        ok "Jellyfin container started"
    fi

    echo
    echo -e "${BOLD}Next steps:${RESET}"
    echo -e "  1. Open ${CYAN}http://localhost:$PORT${RESET} and complete the setup wizard"
    echo -e "  2. Run ${CYAN}$0 server${RESET} to start the federation server"
    echo -e "  3. Register a second server in an incognito window to test federation"
}

cmd_start() {
    heading "Starting dev environment"

    # Start federation server first so plugin connects on first try
    cmd_server

    if container_exists && ! container_running; then
        reset_plugin_status
        info "Starting Jellyfin container…"
        docker start "$CONTAINER" >/dev/null
        ensure_local_container_msquic
        ok "Jellyfin started"
    elif container_running; then
        ok "Jellyfin already running"
    else
        err "Container not found — run '$0 setup' first"
        exit 1
    fi
}

cmd_stop() {
    heading "Stopping dev environment"

    if container_running; then
        info "Stopping Jellyfin…"
        docker stop "$CONTAINER" >/dev/null
        ok "Jellyfin stopped"
    else
        info "Jellyfin not running"
    fi

    if server_running; then
        info "Stopping federation server (PID $(cat "$SERVER_PID_FILE"))…"
        kill "$(cat "$SERVER_PID_FILE")" 2>/dev/null || true
        rm -f "$SERVER_PID_FILE"
        ok "Federation server stopped"
    else
        info "Federation server not running"
    fi
}

cmd_restart() {
    heading "Restarting dev environment"

    if container_exists; then
        reset_plugin_status
        info "Restarting Jellyfin…"
        docker restart "$CONTAINER" >/dev/null
        ensure_local_container_msquic
        ok "Jellyfin restarted"
    else
        err "Container not found — run '$0 setup' first"
        exit 1
    fi
}

cmd_server() {
    local db="$REPO_DIR/src/JellyFederation.Server/federation.db"
    if server_running; then
        ok "Federation server already running (PID $(cat "$SERVER_PID_FILE"))"
        return
    fi

    build_frontend

    # Kill anything still on the port
    lsof -ti:$FEDERATION_PORT | xargs kill -9 2>/dev/null || true

    info "Starting federation server on :$FEDERATION_PORT…"
    nohup env ConnectionStrings__Default="Data Source=$db" \
        dotnet run --project "$REPO_DIR/src/JellyFederation.Server" \
        > "$SERVER_LOG" 2>&1 &
    echo $! > "$SERVER_PID_FILE"
    ok "Federation server started (PID $!, log: $SERVER_LOG)"
    wait_healthy "http://localhost:$FEDERATION_PORT/swagger/v1/swagger.json" "federation server" || \
        wait_healthy "http://localhost:$FEDERATION_PORT/" "federation server"
}

cmd_deploy() {
    heading "Deploying updated plugin"

    build_plugin
    deploy_local_from_tmp
}

cmd_build() {
    local target="${1:-all}"
    shift || true

    heading "Building ($target)"

    case "$target" in
        all|solution)
            dotnet build --solution "$REPO_DIR/JellyFederation.slnx" "$@"
            ;;
        server)
            dotnet build "$REPO_DIR/src/JellyFederation.Server/JellyFederation.Server.csproj" "$@"
            ;;
        plugin)
            dotnet build "$REPO_DIR/src/JellyFederation.Plugin/JellyFederation.Plugin.csproj" "$@"
            ;;
        web)
            build_frontend
            ;;
        *)
            err "Unknown build target: $target. Use all|solution|server|plugin|web"
            exit 1
            ;;
    esac
}

cmd_test() {
    local target="${1:-all}"
    shift || true

    for arg in "$@"; do
        if [[ "$arg" == "--filter" || "$arg" == --filter=* ]]; then
            err "'--filter' is a VSTest argument and not supported by Microsoft Testing Platform in this repo."
            err "Use one of: --filter-query, --filter-class, --filter-method, --filter-trait"
            err "Example: $0 test server --filter-query "/**/*/*/*[name~SignalRWorkflowTests]""
            exit 2
        fi
    done

    heading "Testing ($target)"

    case "$target" in
        all|solution)
            dotnet test --solution "$REPO_DIR/JellyFederation.slnx" "$@"
            ;;
        server)
            dotnet test --project "$REPO_DIR/tests/JellyFederation.Server.Tests/JellyFederation.Server.Tests.csproj" "$@"
            ;;
        plugin)
            dotnet test --project "$REPO_DIR/tests/JellyFederation.Plugin.Tests/JellyFederation.Plugin.Tests.csproj" "$@"
            ;;
        *)
            err "Unknown test target: $target. Use all|solution|server|plugin"
            exit 1
            ;;
    esac
}

cmd_logs() {
    local target="${1:-jellyfin}"
    case "$target" in
        jellyfin|jf)
            docker logs "$CONTAINER" --tail 50 -f 2>&1 | grep --color=never -i "JellyFederation\|ERR\|federation\|synced\|plugin" || \
            docker logs "$CONTAINER" --tail 50 -f
            ;;
        server|srv)
            tail -f "$SERVER_LOG"
            ;;
        *)
            err "Unknown target: $target. Use 'jellyfin' or 'server'"
            exit 1
            ;;
    esac
}

cmd_status() {
    heading "Dev environment status"

    # Jellyfin
    if container_running; then
        echo -e "  Jellyfin   ${GREEN}running${RESET}  →  http://localhost:$PORT"
    elif container_exists; then
        echo -e "  Jellyfin   ${YELLOW}stopped${RESET}"
    else
        echo -e "  Jellyfin   ${RED}not set up${RESET}  (run '$0 setup')"
    fi

    # Federation server
    if server_running; then
        echo -e "  Fed Server ${GREEN}running${RESET}  →  http://localhost:$FEDERATION_PORT  (PID $(cat "$SERVER_PID_FILE"))"
    else
        echo -e "  Fed Server ${RED}stopped${RESET}"
    fi

    # Plugin
    if [[ -f "$PLUGIN_DIR/JellyFederation.Plugin.dll" ]]; then
        local mtime
        mtime=$(date -r "$PLUGIN_DIR/JellyFederation.Plugin.dll" "+%Y-%m-%d %H:%M")
        echo -e "  Plugin     ${GREEN}installed${RESET}  (built $mtime)"
    else
        echo -e "  Plugin     ${RED}not installed${RESET}"
    fi

    echo
}

cmd_seed_config() {
    local db="$REPO_DIR/src/JellyFederation.Server/federation.db"
    local cfg_dir="$DATA_DIR/config/plugins/configurations"
    local cfg_file="$cfg_dir/JellyFederation.Plugin.xml"
    local host_ip
    host_ip=$(host_lan_ip)
    local fed_url="${1:-http://${host_ip}:$FEDERATION_PORT}"
    local preferred_name="${2:-dev}"
    local otlp_url="${3:-http://host.docker.internal:4317}"

    if [[ ! -f "$db" ]]; then
        err "Federation database not found at $db — is the server set up?"
        exit 1
    fi

    read -r SERVER_ID SERVER_API_KEY SERVER_NAME < <(db_get_server "$db" "$preferred_name")

    if [[ -z "$SERVER_ID" ]]; then
        err "No servers found in database — register one via the frontend first"
        exit 1
    fi

    mkdir -p "$cfg_dir"
    sudo tee "$cfg_file" > /dev/null << EOF
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <FederationServerUrl>${fed_url}</FederationServerUrl>
  <ServerId>${SERVER_ID}</ServerId>
  <ApiKey>${SERVER_API_KEY}</ApiKey>
  <ServerName>${SERVER_NAME}</ServerName>
  <DownloadDirectory>/media/federation</DownloadDirectory>
  <OverridePublicIp>${host_ip}</OverridePublicIp>
  <HolePunchPort>7777</HolePunchPort>
  <PreferQuicForLargeFiles>true</PreferQuicForLargeFiles>
  <LargeFileQuicThresholdBytes>104857600</LargeFileQuicThresholdBytes>
  <TelemetryServiceName>jellyfederation-plugin-local</TelemetryServiceName>
  <TelemetryOtlpEndpoint>${otlp_url}</TelemetryOtlpEndpoint>
  <TelemetrySamplingRatio>1</TelemetrySamplingRatio>
  <EnableTracing>true</EnableTracing>
  <EnableMetrics>true</EnableMetrics>
  <EnableLogs>true</EnableLogs>
  <RedactionEnabled>true</RedactionEnabled>
</PluginConfiguration>
EOF

    ok "Config written for server '${SERVER_NAME}' (${SERVER_ID})"
    ok "  URL: ${fed_url}"
    ok "  OTLP: ${otlp_url}"
    ok "  File: $cfg_file"

    if container_running; then
        info "Restarting Jellyfin to apply config…"
        docker restart "$CONTAINER" >/dev/null
        ensure_local_container_msquic
        ok "Jellyfin restarted"
    fi
}

cmd_deploy_test() {
    local test_host="${1:-$TEST_HOST_DEFAULT}"
    local test_user="${2:-$TEST_USER_DEFAULT}"

    heading "Deploying plugin to Test server ($test_host)"

    build_plugin
    deploy_remote_from_tmp "$test_host" "$test_user"
}

cmd_seed_config_remote() {
    local test_host="${1:-$TEST_HOST_DEFAULT}"
    local test_user="${2:-$TEST_USER_DEFAULT}"
    local db="$REPO_DIR/src/JellyFederation.Server/federation.db"
    local host_ip
    host_ip=$(host_lan_ip)
    local fed_url="${3:-http://${host_ip}:$FEDERATION_PORT}"
    local remote_preferred_name="${4:-test}"
    local otlp_url="${5:-http://${host_ip}:4317}"

    if [[ ! -f "$db" ]]; then
        err "Federation database not found at $db — is the server set up?"
        exit 1
    fi

    read -r LOCAL_SERVER_ID _ _ < <(db_get_server "$db" "dev")
    read -r SERVER_ID SERVER_API_KEY SERVER_NAME < <(db_get_server "$db" "$remote_preferred_name" "${LOCAL_SERVER_ID:-}")

    if [[ -z "$SERVER_ID" ]]; then
        err "No remote server entry found in database — register remote server first"
        exit 1
    fi

    local tmp_cfg
    tmp_cfg="$(mktemp /tmp/jf-remote-plugin-config.XXXXXX.xml)"
    cat > "$tmp_cfg" << EOF
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <FederationServerUrl>${fed_url}</FederationServerUrl>
  <ServerId>${SERVER_ID}</ServerId>
  <ApiKey>${SERVER_API_KEY}</ApiKey>
  <ServerName>${SERVER_NAME}</ServerName>
  <DownloadDirectory>/media/federation</DownloadDirectory>
  <OverridePublicIp>${test_host}</OverridePublicIp>
  <HolePunchPort>7777</HolePunchPort>
  <PreferQuicForLargeFiles>true</PreferQuicForLargeFiles>
  <LargeFileQuicThresholdBytes>104857600</LargeFileQuicThresholdBytes>
  <TelemetryServiceName>jellyfederation-plugin-remote</TelemetryServiceName>
  <TelemetryOtlpEndpoint>${otlp_url}</TelemetryOtlpEndpoint>
  <TelemetrySamplingRatio>1</TelemetrySamplingRatio>
  <EnableTracing>true</EnableTracing>
  <EnableMetrics>true</EnableMetrics>
  <EnableLogs>true</EnableLogs>
  <RedactionEnabled>true</RedactionEnabled>
</PluginConfiguration>
EOF

    info "Seeding remote plugin config on $test_host…"
    scp "$tmp_cfg" "${test_user}@${test_host}:/tmp/JellyFederation.Plugin.xml"
    ssh "${test_user}@${test_host}" '
set -e
if [ -d "/opt/media/jellyfin/plugins/configurations" ] || [ -d "/opt/media/jellyfin/plugins" ]; then
  cfg_dir="/opt/media/jellyfin/plugins/configurations"
else
  cfg_dir="/opt/media/jellyfin/config/plugins/configurations"
fi
mkdir -p "$cfg_dir"
install -m 0644 /tmp/JellyFederation.Plugin.xml "$cfg_dir/JellyFederation.Plugin.xml"
rm -f /tmp/JellyFederation.Plugin.xml
cd "'"$TEST_COMPOSE_DIR"'"
docker compose restart jellyfin
'
    rm -f "$tmp_cfg"
    ok "Remote config seeded and Jellyfin restarted on $test_host"
    ok "  OTLP: ${otlp_url}"
}

cmd_stack_up() {
    heading "Starting compose federation stack"

    # Federation server runs locally (dotnet run), not in compose.
    cmd_server
    compose up -d lgtm
    ok "Observability stack started"
    echo -e "  Federation server: ${CYAN}http://localhost:$FEDERATION_PORT${RESET}"
    echo -e "  Jellyfin local:    ${CYAN}http://localhost:$PORT${RESET} (standalone container)"
    echo -e "  Jellyfin remote:   ${CYAN}http://192.168.2.192${RESET} (production/test host)"
    echo -e "  Grafana/LGTM:      ${CYAN}http://localhost:3000${RESET}"
}

cmd_stack_down() {
    heading "Stopping compose federation stack"
    compose down
    if server_running; then
        info "Stopping federation server (PID $(cat "$SERVER_PID_FILE"))…"
        kill "$(cat "$SERVER_PID_FILE")" 2>/dev/null || true
        rm -f "$SERVER_PID_FILE"
        ok "Federation server stopped"
    fi
    ok "Observability stack stopped"
}

cmd_stack_status() {
    heading "Compose federation stack status"
    compose ps
}

cmd_stack_logs() {
    local target="${1:-server}"
    case "$target" in
        server|federation-server) tail -f "$SERVER_LOG" ;;
        local|jellyfin-local) cmd_logs jellyfin ;;
        remote|jellyfin-remote)
            err "Remote Jellyfin logs are not local. Use SSH on 192.168.2.192."
            exit 1
            ;;
        lgtm|grafana) compose logs -f lgtm ;;
        all) compose logs -f ;;
        *)
            err "Unknown target: $target. Use server|local|remote|lgtm|all"
            exit 1
            ;;
    esac
}

cmd_stack_restart() {
    heading "Restarting local federation + observability stack"
    if server_running; then
        info "Stopping federation server (PID $(cat "$SERVER_PID_FILE"))…"
        kill "$(cat "$SERVER_PID_FILE")" 2>/dev/null || true
        rm -f "$SERVER_PID_FILE"
    fi
    cmd_server
    compose up -d lgtm
    ok "Federation server and LGTM restarted"
}

cmd_stack_deploy() {
    heading "Deploying plugin to local and test Jellyfin"
    build_plugin
    deploy_local_from_tmp
    deploy_remote_from_tmp "$TEST_HOST_DEFAULT" "$TEST_USER_DEFAULT"
    ok "Plugin deployed to local and remote Jellyfin"
}

cmd_refresh() {
    local test_host="${1:-$TEST_HOST_DEFAULT}"
    local test_user="${2:-$TEST_USER_DEFAULT}"
    local local_server_name="${3:-dev}"
    local remote_server_name="${4:-test}"
    local host_ip
    host_ip=$(host_lan_ip)
    local fed_url="http://${host_ip}:$FEDERATION_PORT"

    heading "Refreshing local + remote federation setup"

    info "Rebuilding plugin artifacts…"
    build_plugin

    info "Rebuilding federation server…"
    build_frontend
    dotnet build "$REPO_DIR/src/JellyFederation.Server/JellyFederation.Server.csproj" -v minimal

    info "Restarting local federation server + LGTM…"
    cmd_stack_restart

    info "Deploying plugin to local Jellyfin…"
    deploy_local_from_tmp

    info "Deploying plugin to remote Jellyfin ($test_host)…"
    deploy_remote_from_tmp "$test_host" "$test_user"

    info "Seeding local plugin config from federation DB…"
    cmd_seed_config "$fed_url" "$local_server_name" "http://host.docker.internal:4317"

    info "Seeding remote plugin config from federation DB…"
    cmd_seed_config_remote "$test_host" "$test_user" "$fed_url" "$remote_server_name" "http://${host_ip}:4317"

    ok "Refresh complete"
}

cmd_reset() {
    heading "Resetting dev environment"
    warn "This will delete all Jellyfin data at $DATA_DIR"
    read -rp "  Are you sure? [y/N] " confirm
    [[ "${confirm,,}" == "y" ]] || { info "Aborted"; exit 0; }

    cmd_stop 2>/dev/null || true

    if container_exists; then
        docker rm -f "$CONTAINER" >/dev/null
        ok "Container removed"
    fi

    if [[ -d "$DATA_DIR" ]]; then
        rm -rf "$DATA_DIR"
        ok "Data directory removed"
    fi

    ok "Reset complete — run '$0 setup' to start fresh"
}

cmd_open() {
    local url="http://localhost:$PORT"
    info "Opening $url"
    if command -v xdg-open &>/dev/null; then
        xdg-open "$url"
    elif command -v open &>/dev/null; then
        open "$url"
    else
        echo "$url"
    fi
}

# ── usage ─────────────────────────────────────────────────────────────────────
usage() {
    echo -e "${BOLD}Usage:${RESET} $0 <command> [args]"
    echo -e "       ${BOLD}or:${RESET} $0    (runs full refresh default)"
    echo
    echo -e "${BOLD}Commands:${RESET}"
    echo "  setup               First-time setup: build plugin, create dirs, start Jellyfin"
    echo "  start               Start Jellyfin + federation server"
    echo "  stop                Stop everything"
    echo "  restart             Restart Jellyfin container"
    echo "  server              Start federation server only"
    echo "  deploy              Rebuild plugin and redeploy to running Jellyfin"
    echo "  build [target] [dotnet args...]"
    echo "                      Build target: all|solution|server|plugin|web (default: all)"
    echo "  test [target] [dotnet test args...]"
    echo "                      Run tests with Microsoft Testing Platform"
    echo "                      Target: all|solution|server|plugin (default: all)"
    echo "                      Examples:"
    echo "                        $0 test server --list-tests"
    echo "                        $0 test server --filter-class \"*SignalRWorkflowTests\""
    echo "                        $0 test server --filter-query \"/**/*/*/*[name~SignalRWorkflowTests]\""
    echo "                        $0 test server -- --max-parallel-test-modules 1"
    echo "  deploy-test [host] [user]  Deploy plugin to production Test Jellyfin via SCP (default: root@192.168.2.192)"
    echo "  seed-config [url] [name] [otlp-url]   Write local plugin config XML from federation DB"
    echo "  seed-config-remote [host] [user] [url] [name] [otlp-url]"
    echo "                      Write remote plugin config XML from federation DB and restart remote Jellyfin"
    echo "  status              Show status of all components"
    echo "  logs [target]       Tail logs — target: jellyfin (default) or server"
    echo
    echo "  stack-up            Start local federation server + LGTM docker-compose stack"
    echo "  stack-down          Stop LGTM docker-compose stack and local federation server"
    echo "  stack-status        Show docker-compose service status"
    echo "  stack-logs [target] Tail logs (server|local|lgtm|all)"
    echo "  stack-restart       Restart local federation server + LGTM"
    echo "  stack-deploy        Rebuild and deploy plugin to local + remote Jellyfin"
    echo "  refresh [host] [user] [local-name] [remote-name]"
    echo "                      Full refresh: rebuild plugin+server, restart local server,"
    echo "                      deploy local+remote plugin, seed local+remote configs"
    echo "  open                Open Jellyfin in browser"
    echo "  reset               Destroy everything and start fresh"
    echo
}

# ── dispatch ──────────────────────────────────────────────────────────────────
case "${1:-}" in
    "")          cmd_refresh ;;
    setup)       cmd_setup ;;
    start)       cmd_start ;;
    stop)        cmd_stop ;;
    restart)     cmd_restart ;;
    server)      cmd_server ;;
    deploy)      cmd_deploy ;;
    build)       cmd_build "${2:-all}" "${@:3}" ;;
    test)        cmd_test "${2:-all}" "${@:3}" ;;
    deploy-test) cmd_deploy_test "${2:-}" "${3:-}" ;;
    seed-config) cmd_seed_config "${2:-}" "${3:-}" "${4:-}" ;;
    seed-config-remote) cmd_seed_config_remote "${2:-}" "${3:-}" "${4:-}" "${5:-}" "${6:-}" ;;
    logs)        cmd_logs "${2:-jellyfin}" ;;
    stack-up)    cmd_stack_up ;;
    stack-down)  cmd_stack_down ;;
    stack-status) cmd_stack_status ;;
    stack-logs)  cmd_stack_logs "${2:-server}" ;;
    stack-restart) cmd_stack_restart ;;
    stack-deploy) cmd_stack_deploy ;;
    refresh)     cmd_refresh "${2:-}" "${3:-}" "${4:-}" "${5:-}" ;;
    status)      cmd_status ;;
    open)        cmd_open ;;
    reset)       cmd_reset ;;
    *)       usage ;;
esac
