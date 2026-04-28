#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'USAGE'
Usage: scripts/coverage.sh [--target <all|server|plugin>] [--no-clean] [--min-line-coverage <percent>] [--help]

Collects test coverage via Microsoft.Testing.Extensions.CodeCoverage (MTP)
and generates merged reports via ReportGenerator.

Outputs:
  - TestResults/server/server.cobertura.xml
  - TestResults/plugin/plugin.cobertura.xml
  - coverage/index.html
  - coverage/Summary.txt
  - coverage/SummaryGithub.md

Options:
  --target <name>             Coverage scope: all (default), server, plugin
  --no-clean                  Keep existing TestResults/ and coverage/
  --min-line-coverage <pct>   Minimum total line coverage gate (default: 80)
  --help                      Show this help text
USAGE
}

log() {
  printf '\n\033[1;34m==> %s\033[0m\n' "$*"
}

fail() {
  printf '\n\033[1;31mERROR: %s\033[0m\n' "$*" >&2
  exit 1
}

target="all"
clean="true"
min_line_coverage="80"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --target)
      target="${2:-}"
      shift 2
      ;;
    --no-clean)
      clean="false"
      shift
      ;;
    --min-line-coverage)
      min_line_coverage="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "Unknown argument: $1"
      ;;
  esac
done

case "$target" in
  all|server|plugin) ;;
  *) fail "Invalid --target '$target'. Use all|server|plugin." ;;
esac

[[ "$min_line_coverage" =~ ^[0-9]+([.][0-9]+)?$ ]] || fail "Invalid --min-line-coverage '$min_line_coverage'. Use a numeric value."

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
cd "$repo_root"

command -v dotnet >/dev/null 2>&1 || fail "dotnet is required but was not found on PATH."

log "Restoring tools"
dotnet tool restore

if [[ "$clean" == "true" ]]; then
  log "Cleaning previous artifacts"
  rm -rf TestResults coverage
fi

run_server="false"
run_plugin="false"
if [[ "$target" == "all" || "$target" == "server" ]]; then run_server="true"; fi
if [[ "$target" == "all" || "$target" == "plugin" ]]; then run_plugin="true"; fi

mkdir -p "$repo_root/TestResults/server" "$repo_root/TestResults/plugin"

if [[ "$run_server" == "true" ]]; then
  log "Running server tests with coverage"
  ./dev.sh test server -- \
    --coverage \
    --coverage-output-format cobertura \
    --coverage-output "$repo_root/TestResults/server/server.cobertura.xml"
fi

if [[ "$run_plugin" == "true" ]]; then
  log "Running plugin tests with coverage"
  ./dev.sh test plugin -- \
    --coverage \
    --coverage-output-format cobertura \
    --coverage-output "$repo_root/TestResults/plugin/plugin.cobertura.xml"
fi

reports=()
[[ -f "TestResults/server/server.cobertura.xml" ]] && reports+=("TestResults/server/server.cobertura.xml")
[[ -f "TestResults/plugin/plugin.cobertura.xml" ]] && reports+=("TestResults/plugin/plugin.cobertura.xml")

[[ ${#reports[@]} -gt 0 ]] || fail "No coverage reports were produced."

IFS=';'
report_paths="${reports[*]}"
unset IFS

log "Generating merged coverage report"
dotnet reportgenerator \
  -reports:"$report_paths" \
  -targetdir:"coverage" \
  -reporttypes:"Html;TextSummary;MarkdownSummaryGithub;Cobertura" \
  -assemblyfilters:"+JellyFederation.*;-JellyFederation.Migrations.*;-*.Tests" \
  -classfilters:"+JellyFederation.*;-Microsoft.*;-System.*" \
  -filefilters:"-**/*.g.cs;-**/*.generated.cs;-**/*.designer.cs;-**/Migrations/**/*"

log "Coverage summary"
if [[ -f coverage/Summary.txt ]]; then
  cat coverage/Summary.txt
else
  echo "No text summary generated."
fi

[[ -f coverage/Cobertura.xml ]] || fail "coverage/Cobertura.xml was not generated."

line_rate=$(sed -n 's/.*line-rate="\([0-9.]*\)".*/\1/p' coverage/Cobertura.xml | head -n 1)
[[ -n "$line_rate" ]] || fail "Unable to read line-rate from coverage/Cobertura.xml"

line_coverage_pct=$(awk -v rate="$line_rate" 'BEGIN { printf "%.2f", rate * 100 }')
log "Line coverage gate"
printf 'Line coverage: %s%% (minimum: %s%%)\n' "$line_coverage_pct" "$min_line_coverage"

if ! awk -v actual="$line_coverage_pct" -v required="$min_line_coverage" 'BEGIN { exit (actual + 0 >= required + 0) ? 0 : 1 }'; then
  fail "Coverage gate failed: line coverage ${line_coverage_pct}% is below required ${min_line_coverage}%"
fi

log "Done"
echo "HTML report: $repo_root/coverage/index.html"