#!/usr/bin/env bash
#
# gh-api.sh — minimal authenticated GitHub REST API helper for the telemetry
# routine. Used instead of the GitHub MCP server so the routine is fully
# self-contained (no MCP dependency; MCP availability is not guaranteed inside
# scheduled runs).
#
# Auth: reads a token from GIT_PAT (or GITHUB_TOKEN). A classic PAT with the
# `repo` scope, or a fine-grained token with Issues: read & write on
# onpaj/Anela.Heblo, is sufficient. No token is stored in the repo.
#
# Egress: needs api.github.com on the environment's network allowlist.
#
# Usage:
#   # Raw GET (path or full URL); prints JSON
#   routines/telemetry-anomaly/gh-api.sh GET '/search/issues?q=repo:onpaj/Anela.Heblo+label:telemetry+in:body+telemetry-signal'
#
#   # Search issues (open AND closed) for a telemetry-signal fingerprint
#   routines/telemetry-anomaly/gh-api.sh find-signal 'req-5xx:Articles/FeedbackList:500'
#
#   # Create an issue (body from a file or stdin)
#   routines/telemetry-anomaly/gh-api.sh create-issue "title" "telemetry,reliability" body.md
#   printf '...' | routines/telemetry-anomaly/gh-api.sh create-issue "title" "telemetry,risk" -
#
set -euo pipefail

REPO="${GH_REPO:-onpaj/Anela.Heblo}"
API="https://api.github.com"
TOKEN="${GIT_PAT:-${GITHUB_TOKEN:-}}"

err() { echo "Error: $*" >&2; exit 1; }
[[ -n "$TOKEN" ]] || err "no token — set GIT_PAT (or GITHUB_TOKEN)."
command -v jq >/dev/null || err "jq is required."

req() {
  # req METHOD PATH_OR_URL [json-body]
  # Retries transient 403/429 (GitHub search rate limiting is 30/min and bursts
  # of dedup searches can trip it) with backoff, so a throttled dedup never
  # turns into a spurious skip or a duplicate filing.
  local method="$1" path="$2" body="${3:-}"
  local url="$path"
  [[ "$path" == http* ]] || url="${API}${path}"
  local args=(-sS --max-time 30 -X "$method"
    -H "Authorization: Bearer ${TOKEN}"
    -H "Accept: application/vnd.github+json"
    -H "X-GitHub-Api-Version: 2022-11-28"
    -w $'\n__HTTP_CODE__%{http_code}')
  [[ -n "$body" ]] && args+=(-d "$body")

  local out code delay=3 attempt
  for attempt in 1 2 3 4; do
    out="$(curl "${args[@]}" "$url")"
    code="${out##*__HTTP_CODE__}"
    if [[ "$code" != "403" && "$code" != "429" ]]; then
      printf '%s' "$out"; return 0
    fi
    [[ $attempt -eq 4 ]] && { printf '%s' "$out"; return 0; }
    echo "GitHub API ${code} (rate limit?) — retrying in ${delay}s..." >&2
    sleep "$delay"; delay=$((delay * 2))
  done
}

emit() {
  local out="$1" code="${1##*__HTTP_CODE__}"
  local payload="${out%__HTTP_CODE__*}"
  echo "$payload"
  [[ "$code" =~ ^2 ]] || err "GitHub API HTTP ${code}."
}

cmd="${1:-}"; shift || true
case "$cmd" in
  GET|DELETE)
    emit "$(req "$cmd" "${1:?path required}")" ;;

  POST|PATCH)
    emit "$(req "$cmd" "${1:?path required}" "${2:-}")" ;;

  find-signal)
    # Search issues (any state) whose body carries the exact fingerprint line.
    sig="${1:?fingerprint required, e.g. req-5xx:Articles/FeedbackList:500}"
    q="repo:${REPO} label:telemetry in:body \"telemetry-signal: ${sig}\""
    enc="$(jq -rn --arg q "$q" '$q|@uri')"
    emit "$(req GET "/search/issues?q=${enc}&per_page=20")" \
      | jq '{total: .total_count,
             matches: [ .items[] | {number, state,
                state_reason: (.state_reason // null),
                title, html_url, closed_at} ]}'
    ;;

  create-issue)
    title="${1:?title required}"; labels="${2:?comma-separated labels required}"; src="${3:?body file or - for stdin}"
    if [[ "$src" == "-" ]]; then body="$(cat)"; else body="$(cat "$src")"; fi
    labels_json="$(jq -rn --arg l "$labels" '($l|split(","))')"
    payload="$(jq -n --arg t "$title" --arg b "$body" --argjson labels "$labels_json" \
      '{title:$t, body:$b, labels:$labels}')"
    emit "$(req POST "/repos/${REPO}/issues" "$payload")" \
      | jq '{number, html_url, state}'
    ;;

  ""|-h|--help)
    sed -n '2,30p' "$0" ;;
  *)
    err "unknown command '${cmd}'. Try: GET, POST, PATCH, DELETE, find-signal, create-issue." ;;
esac
