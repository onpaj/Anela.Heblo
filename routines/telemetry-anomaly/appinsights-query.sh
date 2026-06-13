#!/usr/bin/env bash
#
# appinsights-query.sh — query Azure Application Insights via the REST data-plane API.
#
# Credentials are read from the environment (never hardcode the key):
#   APPINSIGHTS_APP_ID   Application ID (GUID) from the App Insights "API Access" blade
#   APPINSIGHTS_API_KEY  API key with "Read telemetry" permission
#
# In Claude Code on the web, set these as encrypted environment secrets and add
#   api.applicationinsights.io
# to the environment's Custom network-access allowlist (the host is NOT in the
# default Trusted list).
#
# Usage:
#   routines/telemetry-anomaly/appinsights-query.sh --test
#       Run a connectivity + auth self-test.
#
#   routines/telemetry-anomaly/appinsights-query.sh 'requests | take 5'
#       Run an arbitrary KQL query; prints raw JSON.
#
#   routines/telemetry-anomaly/appinsights-query.sh --timespan P1D 'exceptions | summarize count() by type'
#       Same, scoped to an ISO-8601 timespan (default P1D = last 24h).
#
set -euo pipefail

API_HOST="https://api.applicationinsights.io"
TIMESPAN="P1D"

err() { echo "Error: $*" >&2; exit 1; }

[[ -n "${APPINSIGHTS_APP_ID:-}" ]]  || err "APPINSIGHTS_APP_ID is not set."
[[ -n "${APPINSIGHTS_API_KEY:-}" ]] || err "APPINSIGHTS_API_KEY is not set."

if [[ "${1:-}" == "--timespan" ]]; then
  TIMESPAN="${2:?--timespan requires a value, e.g. P1D}"
  shift 2
fi

run_query() {
  local query="$1"
  curl -sS --max-time 30 -G \
    -H "x-api-key: ${APPINSIGHTS_API_KEY}" \
    --data-urlencode "query=${query}" \
    --data-urlencode "timespan=${TIMESPAN}" \
    -w $'\n__HTTP_CODE__%{http_code}' \
    "${API_HOST}/v1/apps/${APPINSIGHTS_APP_ID}/query"
}

if [[ "${1:-}" == "--test" ]]; then
  echo "Testing connection to ${API_HOST} for app ${APPINSIGHTS_APP_ID}..."
  out="$(run_query 'print test_value = 1')" || err "curl failed (network/egress blocked?)."
  code="${out##*__HTTP_CODE__}"
  body="${out%__HTTP_CODE__*}"
  if [[ "$code" == "200" ]]; then
    echo "OK — authenticated and reachable."
    echo "$body"
    exit 0
  fi
  echo "$body" >&2
  case "$code" in
    403) err "HTTP 403 — egress blocked OR key lacks 'Read telemetry'. If you see 'Host not in allowlist', add api.applicationinsights.io to network egress." ;;
    401) err "HTTP 401 — invalid API key." ;;
    404) err "HTTP 404 — check APPINSIGHTS_APP_ID." ;;
    *)   err "HTTP ${code}." ;;
  esac
fi

[[ $# -ge 1 ]] || err "No query provided. See --help/usage in this script header."

out="$(run_query "$1")" || err "curl failed (network/egress blocked?)."
code="${out##*__HTTP_CODE__}"
body="${out%__HTTP_CODE__*}"
echo "$body"
[[ "$code" == "200" ]] || err "HTTP ${code}."
