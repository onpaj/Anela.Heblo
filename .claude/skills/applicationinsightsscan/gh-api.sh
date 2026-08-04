#!/usr/bin/env bash
#
# gh-api.sh — minimal authenticated GitHub REST API helper for the telemetry
# routine. Used instead of the GitHub MCP server so the routine is fully
# self-contained (no MCP dependency; MCP availability is not guaranteed inside
# scheduled runs).
#
# Target repo: auto-detected from `git remote get-url origin` in the current
# checkout (so this script targets whatever repo it is run inside), or set
# explicitly via GH_REPO=owner/repo.
#
# Auth: reads a token from GIT_PAT (or GITHUB_TOKEN). A classic PAT with the
# `repo` scope, or a fine-grained token with Issues: read & write on the
# target repo, is sufficient. No token is stored in the repo.
#
# Egress: needs api.github.com on the environment's network allowlist.
#
# Usage:
#   # Print the auto-detected (or GH_REPO-overridden) target repo
#   .claude/skills/applicationinsightsscan/gh-api.sh repo
#
#   # Raw GET (path or full URL); prints JSON
#   .claude/skills/applicationinsightsscan/gh-api.sh GET '/search/issues?q=repo:owner/repo+label:telemetry+in:body+telemetry-signal'
#
#   # Search issues (open AND closed) for a telemetry-signal fingerprint
#   .claude/skills/applicationinsightsscan/gh-api.sh find-signal 'req-5xx:Orders/Create:500'
#
#   # Create an issue (body from a file or stdin)
#   .claude/skills/applicationinsightsscan/gh-api.sh create-issue "title" "telemetry,reliability" body.md
#   printf '...' | .claude/skills/applicationinsightsscan/gh-api.sh create-issue "title" "telemetry,risk" -
#
set -euo pipefail

detect_repo() {
  local url path
  url="$(git remote get-url origin 2>/dev/null)" || return 1
  [[ "$url" == *github.com* ]] || return 1
  path="${url#*github.com[:/]}"
  path="${path%.git}"
  path="${path%/}"
  [[ -n "$path" && "$path" == */* ]] || return 1
  echo "$path"
}

# GIT_PAT and GITHUB_TOKEN are two names for one effective token (see TOKEN
# below), so capture whether a real one existed *before* the .env fallback
# runs: if it did, that real token must win outright, rather than letting a
# .env-only GIT_PAT outrank a real GITHUB_TOKEN (or vice versa) once merged.
_real_git_pat="${GIT_PAT:-}"
_real_github_token="${GITHUB_TOKEN:-}"

# shellcheck disable=SC1090
source "$(dirname "${BASH_SOURCE[0]}")/env-fallback.sh"
load_env_fallback GH_REPO GIT_PAT GITHUB_TOKEN

REPO="${GH_REPO:-$(detect_repo || true)}"
API="https://api.github.com"
if [[ -n "$_real_git_pat" ]]; then
  TOKEN="$_real_git_pat"
elif [[ -n "$_real_github_token" ]]; then
  TOKEN="$_real_github_token"
else
  TOKEN="${GIT_PAT:-${GITHUB_TOKEN:-}}"
fi

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
  repo)
    [[ -n "$REPO" ]] || err "Could not auto-detect the target GitHub repo (no 'origin' remote pointing at github.com in this checkout). Set GH_REPO=owner/repo explicitly."
    echo "$REPO" ;;

  GET|DELETE)
    emit "$(req "$cmd" "${1:?path required}")" ;;

  POST|PATCH)
    emit "$(req "$cmd" "${1:?path required}" "${2:-}")" ;;

  find-signal)
    # Search issues (any state) whose body carries the exact fingerprint line.
    [[ -n "$REPO" ]] || err "Could not auto-detect the target GitHub repo (no 'origin' remote pointing at github.com in this checkout). Set GH_REPO=owner/repo explicitly."
    sig="${1:?fingerprint required, e.g. req-5xx:Orders/Create:500}"
    q="repo:${REPO} label:telemetry in:body \"telemetry-signal: ${sig}\""
    enc="$(jq -rn --arg q "$q" '$q|@uri')"
    emit "$(req GET "/search/issues?q=${enc}&per_page=20")" \
      | jq '{total: .total_count,
             matches: [ .items[] | {number, state,
                state_reason: (.state_reason // null),
                title, html_url, closed_at} ]}'
    ;;

  create-issue)
    [[ -n "$REPO" ]] || err "Could not auto-detect the target GitHub repo (no 'origin' remote pointing at github.com in this checkout). Set GH_REPO=owner/repo explicitly."
    title="${1:?title required}"; labels="${2:?comma-separated labels required}"; src="${3:?body file or - for stdin}"
    if [[ "$src" == "-" ]]; then body="$(cat)"; else body="$(cat "$src")"; fi
    labels_json="$(jq -rn --arg l "$labels" '($l|split(","))')"
    payload="$(jq -n --arg t "$title" --arg b "$body" --argjson labels "$labels_json" \
      '{title:$t, body:$b, labels:$labels}')"
    emit "$(req POST "/repos/${REPO}/issues" "$payload")" \
      | jq '{number, html_url, state}'
    ;;

  ""|-h|--help)
    sed -n '2,32p' "$0" ;;
  *)
    err "unknown command '${cmd}'. Try: repo, GET, POST, PATCH, DELETE, find-signal, create-issue." ;;
esac
