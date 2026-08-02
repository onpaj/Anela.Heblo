#!/usr/bin/env bash
#
# test-health-digest.sh — deterministic test-health digest from ReportPortal.
#
# The gathering half of the test-health routine: queries ReportPortal for the
# launches in a window plus their failed items, computes findings, and prints a
# Markdown digest ending in a machine-readable STATE line.
#
# Absence of expected data is computed HERE, not left to the agent: a nightly
# that never ran is the failure mode this routine exists to catch.
#
# Usage:
#   test-health-digest.sh [--days N] [--state-only]
#
# Env: RP_API_KEY / RP_ENDPOINT / RP_PROJECT (see rp-query.sh),
#      RP_FIXTURE_DIR, GH_FIXTURE_DIR (offline replay),
#      TEST_HEALTH_STATE_FILE (default $HOME/.cache/test-health/state)
#
# Exit codes: 0 ok | 1 config error | 3 RP unreachable | 4 RP auth rejected
#
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RP="${HERE}/rp-query.sh"
DAYS=7
STATE_ONLY=0
STATE_FILE="${TEST_HEALTH_STATE_FILE:-$HOME/.cache/test-health/state}"

err()  { echo "Error: $*" >&2; exit 1; }
errc() { local c="$1"; shift; echo "Error: $*" >&2; exit "$c"; }

command -v jq >/dev/null || err "jq is required (expected on PATH, e.g. /Users/rem/.local/bin)."
[[ -x "$RP" ]] || err "${RP} not found or not executable."

while [[ $# -gt 0 ]]; do
  case "$1" in
    --days)       DAYS="${2:?--days requires a number}"; shift 2 ;;
    --state-only) STATE_ONLY=1; shift ;;
    -h|--help)    sed -n '2,22p' "$0"; exit 0 ;;
    *)            err "unknown argument '$1'." ;;
  esac
done

# BSD date first (macOS host), GNU date as fallback.
day_offset() { # day_offset N -> the UTC date N days ago as YYYY-MM-DD
  date -u -v-"${1}"d +%Y-%m-%d 2>/dev/null || date -u -d "${1} days ago" +%Y-%m-%d 2>/dev/null \
    || err "neither BSD nor GNU date available."
}

TODAY="$(date -u +%Y-%m-%d)"
mkdir -p "$(dirname "$STATE_FILE")" 2>/dev/null || true

state_field() { # state_field <key> -> value from the state file, or empty
  [[ -f "$STATE_FILE" ]] || return 0
  sed -n "s/^$1=//p" "$STATE_FILE" | head -1
}

# A routine that cannot run must get LOUDER, not quieter. telemetry-anomaly
# reported "done" every day for weeks while entirely broken, because an
# unchanged state produced an identical, deduplicated non-event. Counting
# consecutive error days makes the state change daily, so harness dispatches
# a fresh, escalating task instead of swallowing it.
record_error_and_exit() { # record_error_and_exit <exit-code> <message>
  local code="$1" msg="$2" prev_date prev_n yesterday n
  prev_date="$(state_field lastErrorDate)"
  prev_n="$(state_field errDays)"; [[ -n "$prev_n" ]] || prev_n=0
  yesterday="$(day_offset 1)"
  if [[ "$prev_date" == "$TODAY" ]]; then n="$prev_n"          # re-run same day
  elif [[ "$prev_date" == "$yesterday" ]]; then n=$((prev_n + 1))
  else n=1; fi
  printf 'lastErrorDate=%s\nerrDays=%s\n' "$TODAY" "$n" > "$STATE_FILE" 2>/dev/null || true
  echo "STATE: error=${code}:errdays=${n}"
  echo "Error: ${msg} (exit ${code}, consecutive error days: ${n})" >&2
  exit "$code"
}

clear_error_days() {
  printf 'lastErrorDate=\nerrDays=0\n' > "$STATE_FILE" 2>/dev/null || true
}

# "Now" is overridable so that fixtures can pin it. Fixtures carry absolute
# timestamps; a window computed from wall-clock now would slide past them within
# days and the suite would rot into a false pass — launches silently filtered
# out, zero findings, everything "green". Tests set TEST_HEALTH_NOW_MS.
NOW_MS="${TEST_HEALTH_NOW_MS:-$(( $(date -u +%s) * 1000 ))}"
WINDOW_START_MS=$(( NOW_MS - DAYS * 86400 * 1000 ))

# ---------------------------------------------------------------- gather ----
# One page of 300 launches, newest first, then filter to the window with jq.
# Filtering client-side keeps us off RP's filter-parameter grammar, which is
# version-sensitive and unverified against this instance.
raw_launches="$("$RP" '/launch?page.size=300&page.sort=startTime,DESC')"; rc=$?
if [[ $rc -ne 0 ]]; then
  # 3 = unreachable, 4 = auth. Either way we know NOTHING about test presence,
  # so we must not emit silence findings. Record the error day and stop.
  record_error_and_exit "$rc" "could not read launches from ReportPortal"
fi

launches="$(printf '%s' "$raw_launches" | jq --argjson since "$WINDOW_START_MS" '
  [ .content[]?
    | select(.startTime >= $since)
    | { id, name, number, status, startTime,
        layer:  ((.attributes[]? | select(.key=="layer")  | .value) // "unknown"),
        module: ((.attributes[]? | select(.key=="module") | .value) // "-"),
        branch: ((.attributes[]? | select(.key=="branch") | .value) // "unknown"),
        ci:     ((.attributes[]? | select(.key=="ci")     | .value) // "-"),
        total:  (.statistics.executions.total   // 0),
        passed: (.statistics.executions.passed  // 0),
        failed: (.statistics.executions.failed  // 0),
        skipped:(.statistics.executions.skipped // 0) }
    | select(.branch == "main" or .branch == "unknown") ]
')" || err "could not parse the launch payload as JSON."

# Failed items per launch. Cheap: only failures are fetched, never full item
# lists. Pass/fail history per test is then derived from set membership.
failed_items='[]'
for id in $(printf '%s' "$launches" | jq -r '.[].id'); do
  p="/item?filter.eq.launchId=${id}&filter.in.status=FAILED,INTERRUPTED&page.size=300"
  body="$("$RP" "$p")" || errc $? "could not read failed items for launch ${id}."
  chunk="$(printf '%s' "$body" | jq --argjson lid "$id" '
    [ .content[]? | { launchId: $lid,
                      name: (.name // "unknown"),
                      path: (.pathNames.itemPaths // [] | map(.name) | join(" > ")),
                      status: (.status // "FAILED"),
                      issue: ((.issue.comment // "") ),
                      error: ((.description // "") | split("\n")[0] // "") } ]
  ')" || err "could not parse items for launch ${id}."
  failed_items="$(jq -n --argjson a "$failed_items" --argjson b "$chunk" '$a + $b')"
done

# --------------------------------------------------------------- findings ---
# Findings are accumulated as JSON objects: {category, layer, module, fingerprint, headline, detail}
findings='[]'
add_finding() { findings="$(jq -n --argjson a "$findings" --argjson b "$1" '$a + [$b]')"; }

# (Tasks 4 and 5 append presence, shrink, regression, flaky and chronic
# detection here. Task 3 establishes inventory and plumbing only.)

# ------------------------------------------------------------------ state ---
finding_count="$(printf '%s' "$findings" | jq 'length')"
state_body="$(printf '%s' "$findings" | jq -S -c '[ .[] | .fingerprint ] | sort')"
state_hash="$(printf '%s' "$state_body" | shasum -a 256 | cut -c1-16)"

# Reaching here means the gather succeeded, so the error streak is over.
clear_error_days
STATE="findings=${finding_count}:${state_hash}:errdays=0"

if [[ "$STATE_ONLY" -eq 1 ]]; then
  echo "STATE: ${STATE}"
  exit 0
fi

# ----------------------------------------------------------------- output ---
launch_count="$(printf '%s' "$launches" | jq 'length')"

cat <<EOF
# Test-health digest

- **Window:** last ${DAYS} days (since $(date -u -r $((WINDOW_START_MS/1000)) +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || echo "${WINDOW_START_MS}ms"))
- **Generated:** $(date -u +%Y-%m-%dT%H:%M:%SZ)
- **Source:** ReportPortal project ${RP_PROJECT:-heblo}
- **Launches in window:** ${launch_count}

> Deterministic digest. Absence of expected data is computed here and appears
> as a finding — do not infer it yourself, and do not treat a missing section
> as "everything is fine".

## 1. Launch inventory

EOF

printf '%s' "$launches" | jq -r '
  if length == 0 then "_(no launches in window)_"
  else
    "| launch | layer | module | # | status | total | passed | failed | skipped | started |",
    "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ( sort_by(.layer, .module, -.startTime)[]
      | "| \(.name) | \(.layer) | \(.module) | \(.number) | \(.status) | \(.total) | \(.passed) | \(.failed) | \(.skipped) | \(.startTime) |" )
  end'

echo
echo "## 2. Failed items in window"
echo
printf '%s' "$failed_items" | jq -r '
  if length == 0 then "_(none)_"
  else
    "| launch | test | error (first line) |",
    "| --- | --- | --- |",
    ( .[] | "| \(.launchId) | \(.path) > \(.name) | \(.error | .[0:160]) |" )
  end'

echo
echo "## 3. Findings"
echo
echo "FINDINGS: ${finding_count}"
echo
printf '%s' "$findings" | jq -r '
  if length == 0 then "_(none — nothing to file)_"
  else ( .[] | "- **\(.category)** `\(.fingerprint)` — \(.headline)\n  \(.detail)" )
  end'

echo
echo "STATE: ${STATE}"
