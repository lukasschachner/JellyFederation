#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'USAGE'
Usage: scripts/quality-gates.sh [--dry-run] [--help]

Runs the repository quality gates documented in docs/testing/07-quality-gates.md:
  1. dotnet build JellyFederation.slnx --no-restore
  2. dotnet test --project tests/JellyFederation.Server.Tests/JellyFederation.Server.Tests.csproj --no-restore
  3. slopwatch analyze --fail-on warning

Options:
  --dry-run  Print the gates without executing them.
  --help     Show this help text.
USAGE
}

log() {
  printf '\n\033[1;34m==> %s\033[0m\n' "$*"
}

fail() {
  printf '\n\033[1;31mERROR: %s\033[0m\n' "$*" >&2
  exit 1
}

run_gate() {
  local name="$1"
  shift

  log "$name"
  printf '+ '
  printf '%q ' "$@"
  printf '\n'

  if [[ "$dry_run" == "true" ]]; then
    return 0
  fi

  "$@"
}

dry_run="false"

for arg in "$@"; do
  case "$arg" in
    --dry-run)
      dry_run="true"
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "Unknown argument: $arg"
      ;;
  esac
done

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
cd "$repo_root"

if [[ "$dry_run" != "true" ]]; then
  command -v dotnet >/dev/null 2>&1 || fail "dotnet is required but was not found on PATH."
  command -v slopwatch >/dev/null 2>&1 || fail "slopwatch is required but was not found on PATH. Run 'dotnet tool restore' or install Slopwatch.Cmd."
fi

log "Quality gates starting"
printf 'Repository: %s\n' "$repo_root"

run_gate "Build solution" \
  dotnet build JellyFederation.slnx --no-restore

run_gate "Run server tests" \
  dotnet test --project tests/JellyFederation.Server.Tests/JellyFederation.Server.Tests.csproj --no-restore

run_gate "Run Slopwatch" \
  slopwatch analyze --fail-on warning

log "Quality gates completed successfully"
