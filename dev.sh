#!/usr/bin/env bash
# JellyFederation dev environment manager
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_DIR="/tmp/jellyfin-dev"
CONTAINER="jellyfin-dev"
PORT=8097
PLUGIN_ID="a1b2c3d4-e5f6-7890-abcd-ef1234567890"
PLUGIN_DIR="$DATA_DIR/config/plugins/JellyFederation_1.0.0.0"
SERVER_PID_FILE="/tmp/jf-server.pid"
SERVER_LOG="/tmp/jf-server.log"
FEDERATION_PORT=5264

# ── colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'

info()    { echo -e "${CYAN}▶${RESET} $*"; }
ok()      { echo -e "${GREEN}✓${RESET} $*"; }
warn()    { echo -e "${YELLOW}⚠${RESET} $*"; }
err()     { echo -e "${RED}✗${RESET} $*" >&2; }
heading() { echo -e "\n${BOLD}$*${RESET}"; }

# ── helpers ───────────────────────────────────────────────────────────────────
build_plugin() {
    info "Building plugin…"
    dotnet publish "$REPO_DIR/src/JellyFederation.Plugin" -c Release -o /tmp/jf-plugin -v q 2>&1 \
        | grep -v "^$" | grep -v "^Build" | grep -v "Determining" | grep -v "All projects" || true
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
    mkdir -p "$PLUGIN_DIR"
    cp /tmp/jf-plugin/*.dll "$PLUGIN_DIR/"
    cat > "$PLUGIN_DIR/meta.json" << EOF
{"Id":"$PLUGIN_ID","Name":"JellyFederation","Version":"1.0.0.0","Status":"Active"}
EOF
    ok "Plugin installed → $PLUGIN_DIR"
}

reset_plugin_status() {
    if [[ -f "$PLUGIN_DIR/meta.json" ]]; then
        python3 -c "
import json, pathlib
p = pathlib.Path('$PLUGIN_DIR/meta.json')
d = json.loads(p.read_text())
d['Status'] = 'Active'
p.write_text(json.dumps(d))
"
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

# ── commands ──────────────────────────────────────────────────────────────────
cmd_setup() {
    heading "Setting up JellyFederation dev environment"

    # Build plugin
    build_plugin
    install_plugin

    # Create data dirs
    mkdir -p "$DATA_DIR"/{config,cache,media/movies,media/series,media/music}
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
            -v "$DATA_DIR/config:/config" \
            -v "$DATA_DIR/cache:/cache" \
            -v "$DATA_DIR/media:/media" \
            jellyfin/jellyfin >/dev/null
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
        ok "Jellyfin restarted"
    else
        err "Container not found — run '$0 setup' first"
        exit 1
    fi
}

cmd_server() {
    if server_running; then
        ok "Federation server already running (PID $(cat "$SERVER_PID_FILE"))"
        return
    fi

    build_frontend

    # Kill anything still on the port
    lsof -ti:$FEDERATION_PORT | xargs kill -9 2>/dev/null || true

    info "Starting federation server on :$FEDERATION_PORT…"
    nohup dotnet run --project "$REPO_DIR/src/JellyFederation.Server" \
        > "$SERVER_LOG" 2>&1 &
    echo $! > "$SERVER_PID_FILE"
    ok "Federation server started (PID $!, log: $SERVER_LOG)"
}

cmd_deploy() {
    heading "Deploying updated plugin"

    build_plugin
    install_plugin
    reset_plugin_status

    if container_running; then
        info "Restarting Jellyfin to pick up new plugin…"
        docker restart "$CONTAINER" >/dev/null
        ok "Jellyfin restarted"
    else
        warn "Jellyfin not running — start it with '$0 start'"
    fi
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
    local fed_url="${1:-http://192.168.2.169:$FEDERATION_PORT}"
    # Auto-detect host LAN IP for Docker NAT override
    local host_ip
    host_ip=$(ip route get 1.1.1.1 2>/dev/null | awk '{print $7; exit}' || hostname -I | awk '{print $1}')

    if [[ ! -f "$db" ]]; then
        err "Federation database not found at $db — is the server set up?"
        exit 1
    fi

    # Find a server named 'dev' (or the most recently registered one)
    read -r SERVER_ID SERVER_API_KEY SERVER_NAME < <(python3 - "$db" << 'PYEOF'
import sqlite3, sys
conn = sqlite3.connect(sys.argv[1])
row = conn.execute(
    "SELECT Id, ApiKey, Name FROM Servers WHERE lower(Name)='dev' "
    "ORDER BY RegisteredAt DESC LIMIT 1"
).fetchone()
if not row:
    row = conn.execute(
        "SELECT Id, ApiKey, Name FROM Servers ORDER BY RegisteredAt DESC LIMIT 1"
    ).fetchone()
if row:
    print(row[0], row[1], row[2])
conn.close()
PYEOF
)

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
</PluginConfiguration>
EOF

    ok "Config written for server '${SERVER_NAME}' (${SERVER_ID})"
    ok "  URL: ${fed_url}"
    ok "  File: $cfg_file"

    if container_running; then
        info "Restarting Jellyfin to apply config…"
        docker restart "$CONTAINER" >/dev/null
        ok "Jellyfin restarted"
    fi
}

cmd_deploy_test() {
    local TEST_HOST="${1:-192.168.2.192}"
    local TEST_USER="${2:-root}"
    # Jellyfin runs in Docker on Test; config volume is /opt/media/jellyfin -> /config
    local TEST_PLUGIN_DIR="/opt/media/jellyfin/plugins/JellyFederation_1.0.0.0"
    local TEST_COMPOSE_DIR="/opt/media/jellyfin"

    heading "Deploying plugin to Test server ($TEST_HOST)"

    build_plugin

    info "Copying DLLs to ${TEST_USER}@${TEST_HOST}:${TEST_PLUGIN_DIR}…"
    scp /tmp/jf-plugin/*.dll "${TEST_USER}@${TEST_HOST}:${TEST_PLUGIN_DIR}/"
    ok "DLLs deployed to $TEST_HOST"

    info "Restarting Jellyfin container on $TEST_HOST…"
    ssh "${TEST_USER}@${TEST_HOST}" "cd '${TEST_COMPOSE_DIR}' && docker compose restart jellyfin"
    ok "Jellyfin restarted on $TEST_HOST"
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
    echo
    echo -e "${BOLD}Commands:${RESET}"
    echo "  setup               First-time setup: build plugin, create dirs, start Jellyfin"
    echo "  start               Start Jellyfin + federation server"
    echo "  stop                Stop everything"
    echo "  restart             Restart Jellyfin container"
    echo "  server              Start federation server only"
    echo "  deploy              Rebuild plugin and redeploy to running Jellyfin
  deploy-test [host] [user]  Deploy plugin to production Test Jellyfin via SCP (default: lukasschachner@192.168.2.192)"
    echo "  seed-config [url]   Write plugin config XML from federation DB (skips broken settings UI)"
    echo "  status              Show status of all components"
    echo "  logs [target]       Tail logs — target: jellyfin (default) or server"
    echo "  open                Open Jellyfin in browser"
    echo "  reset               Destroy everything and start fresh"
    echo
}

# ── dispatch ──────────────────────────────────────────────────────────────────
case "${1:-}" in
    setup)       cmd_setup ;;
    start)       cmd_start ;;
    stop)        cmd_stop ;;
    restart)     cmd_restart ;;
    server)      cmd_server ;;
    deploy)      cmd_deploy ;;
    deploy-test) cmd_deploy_test "${2:-}" "${3:-}" ;;
    seed-config) cmd_seed_config "${2:-}" ;;
    logs)        cmd_logs "${2:-jellyfin}" ;;
    status)      cmd_status ;;
    open)        cmd_open ;;
    reset)       cmd_reset ;;
    *)       usage ;;
esac
