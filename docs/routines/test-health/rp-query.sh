#!/usr/bin/env bash
#
# rp-query.sh — query the self-hosted ReportPortal v5 REST API.
#
# Credentials come from the environment (never hardcode, never print):
#   RP_API_KEY   API key minted in the ReportPortal UI (Profile -> API keys)
#   RP_ENDPOINT  REST base INCLUDING /api/v1, e.g. http://nas.tail0cdb23.ts.net:8080/api/v1
#   RP_PROJECT   target project (default: heblo)
#
# Paths are relative to the project (/{project} is prepended) unless --raw.
#
# Offline replay: set RP_FIXTURE_DIR to a directory of recorded responses.
# The fixture file name is the path with a leading '/' stripped and every
# character outside [A-Za-z0-9._-] replaced by '_', plus '.json'.
#
# Exit codes: 0 ok | 1 config/usage error | 3 unreachable | 4 auth rejected
#             5 unexpected HTTP status (404/429/5xx)
#
set -uo pipefail

RP_PROJECT="${RP_PROJECT:-heblo}"

err()  { echo "Error: $*" >&2; exit 1; }
errc() { local c="$1"; shift; echo "Error: $*" >&2; exit "$c"; }

RAW=0
if [[ "${1:-}" == "--raw" ]]; then RAW=1; shift; fi

if [[ "${1:-}" == "--test" ]]; then
  [[ -n "${RP_API_KEY:-}" ]]  || err "RP_API_KEY is not set."
  [[ -n "${RP_ENDPOINT:-}" ]] || err "RP_ENDPOINT is not set."
  echo "Testing ${RP_ENDPOINT} (project ${RP_PROJECT})..."
  body="$("$0" '/launch?page.size=1')"; rc=$?
  case "$rc" in
    0) echo "OK — authenticated and reachable."
       printf '%s' "$body" | head -c 400; echo; exit 0 ;;
    3) errc 3 "ReportPortal unreachable — is the tailnet up and nas:8080 serving?" ;;
    4) errc 4 "ReportPortal rejected the API key (401/403)." ;;
    *) errc "$rc" "ReportPortal query failed." ;;
  esac
fi

PATH_ARG="${1:-}"
[[ -n "$PATH_ARG" ]] || err "no path given. Usage: rp-query.sh [--raw] '/launch?page.size=10'"

# Fixture replay short-circuits everything, including credential checks beyond
# presence, so offline tests need no live server.
if [[ -n "${RP_FIXTURE_DIR:-}" ]]; then
  name="$(printf '%s' "${PATH_ARG#/}" | sed 's/[^A-Za-z0-9._-]/_/g')"
  file="${RP_FIXTURE_DIR}/${name}.json"
  [[ -f "$file" ]] || err "no fixture for path '${PATH_ARG}' (expected ${file})."
  cat "$file"
  exit 0
fi

[[ -n "${RP_API_KEY:-}" ]]  || err "RP_API_KEY is not set."
[[ -n "${RP_ENDPOINT:-}" ]] || err "RP_ENDPOINT is not set."

base="${RP_ENDPOINT%/}"
if [[ "$RAW" -eq 1 ]]; then url="${base}${PATH_ARG}"; else url="${base}/${RP_PROJECT}${PATH_ARG}"; fi

out="$(curl -sS --max-time 45 \
        -H "Authorization: Bearer ${RP_API_KEY}" \
        -H "Accept: application/json" \
        -w $'\n__HTTP_CODE__%{http_code}' \
        "$url" 2>/dev/null)"
curl_rc=$?

# curl 6 (DNS), 7 (connect refused), 28 (timeout), 35 (TLS) all mean "cannot
# reach RP" — which must NEVER be reported as "the tests did not run".
if [[ $curl_rc -ne 0 ]]; then
  errc 3 "cannot reach ReportPortal at ${base} (curl exit ${curl_rc})."
fi

code="${out##*__HTTP_CODE__}"
body="${out%__HTTP_CODE__*}"

case "$code" in
  2*)      printf '%s' "$body"; exit 0 ;;
  401|403) errc 4 "ReportPortal returned HTTP ${code} — API key invalid or lacks project access." ;;
  # Exit 5, NOT 1: a 404/429/5xx means the server said no, which is a wholly
  # different problem from "you forgot to set RP_ENDPOINT". Collapsing the two
  # would send the operator hunting for a missing variable during an RP outage.
  *)       printf '%s' "$body" >&2; errc 5 "ReportPortal returned HTTP ${code}." ;;
esac
