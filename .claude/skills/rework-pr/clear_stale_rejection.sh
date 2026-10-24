#!/usr/bin/env bash
# Before spending a revision on a CI-based `needs-work` rejection, check
# whether the rejection still holds -- and clear it if it does not.
#
#   clear_stale_rejection.sh --pr N
#
# A hygiene rejection (`still-failing`) is a snapshot of CI at one moment.
# If it was wrong at the time (superseded CANCELLED runs read as failures) or
# CI has since gone green, "revising" the PR has nothing to fix: it burns one
# of MAX_REVISION_ATTEMPTS per false rejection until the PR is capped out and
# stranded. This re-reads the live CI state (read-only, via
# hygiene-pr/update_and_wait.sh --check-only) and, only when the latest
# rejection was a `still-failing` one and checks are now green, removes
# `needs-work` and says so.
#
# Emits JSON: {"pr": N, "status": "cleared|holds|pending|not-applicable|error",
#              "detail": "..."}
#   cleared         needs-work removed; nothing left to rework
#   holds           the checks really are failing; rework as usual
#   pending         CI is still running; rework as usual / retry later
#   not-applicable  the latest rejection was not a CI one (review, conflict)
# Always exits 0 once arguments validate.
set -uo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
LIB="$SCRIPT_DIR/../_lib/gh_api.sh"
CHECK="$SCRIPT_DIR/../hygiene-pr/update_and_wait.sh"
NEEDS_WORK_LABEL="needs-work"

PR=""
while [ $# -gt 0 ]; do
  case "$1" in
    --pr) PR="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done
[ -n "$PR" ] || { echo "--pr is required" >&2; exit 1; }

report() {  # status, detail
  jq -n --argjson pr "$PR" --arg status "$1" --arg detail "$2" \
    '{pr: $pr, status: $status, detail: $detail}'
}

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
  url=$(git remote get-url origin 2>/dev/null) || { echo "cannot detect repo: no origin remote" >&2; exit 1; }
  case "$url" in
    *github.com*) ;;
    *) echo "cannot detect repo: origin is not a github.com remote" >&2; exit 1 ;;
  esac
  REPO="${url#*github.com[:/]}"; REPO="${REPO%.git}"; REPO="${REPO%/}"
  [[ "$REPO" == */* ]] || { echo "cannot detect repo: could not parse origin URL" >&2; exit 1; }
fi

if [ -n "${USE_GH_API:-}" ]; then
  comments=$(GH_REPO="$REPO" "$LIB" paginate "repos/$REPO/issues/$PR/comments" 2>&1)
else
  comments=$(gh api --paginate "repos/$REPO/issues/$PR/comments" 2>&1)
fi || { report "error" "could not read PR comments: $comments"; exit 0; }

# The most recent REJECT block decides what kind of rejection this is.
latest_reject=$(echo "$comments" | jq -rs '
  [.[] | if type == "array" then .[] else . end | .body // "" | select(test("verdict:\\s*REJECT"))] | last // ""' 2>/dev/null)
if [ -z "$latest_reject" ]; then
  report "not-applicable" "no REJECT comment found"; exit 0
fi
if ! echo "$latest_reject" | grep -qE '^[[:space:]]*-[[:space:]]+still-failing:'; then
  report "not-applicable" "latest rejection was not a CI (still-failing) rejection"; exit 0
fi

state=$(GH_REPO="$REPO" "$CHECK" --pr "$PR" --check-only 2>&1)
status=$(echo "$state" | jq -r '.status // empty' 2>/dev/null)
case "$status" in
  already-clean) ;;
  still-failing) report "holds" "checks are still failing"; exit 0 ;;
  ci-running)    report "pending" "checks are still running; cannot judge the rejection yet"; exit 0 ;;
  *)             report "error" "could not read current CI state: $state"; exit 0 ;;
esac

body_file=$(mktemp)
printf 'Re-checked before reworking: every current check on this PR is passing, so the earlier CI rejection no longer holds. Clearing `%s`; no revision was made and this does not count as a rework attempt.\n' \
  "$NEEDS_WORK_LABEL" > "$body_file"
if [ -n "${USE_GH_API:-}" ]; then
  GH_REPO="$REPO" "$LIB" pr-comment "$PR" "$body_file" >/dev/null 2>&1 || true
  rm_out=$(GH_REPO="$REPO" "$LIB" pr-edit "$PR" --remove-label "$NEEDS_WORK_LABEL" 2>&1) && rm_ok=1 || rm_ok=0
else
  gh pr comment "$PR" --repo "$REPO" --body-file "$body_file" >/dev/null 2>&1 || true
  rm_out=$(gh pr edit "$PR" --repo "$REPO" --remove-label "$NEEDS_WORK_LABEL" 2>&1) && rm_ok=1 || rm_ok=0
fi
rm -f "$body_file"
if [ "$rm_ok" -ne 1 ]; then
  report "error" "checks are green but could not remove $NEEDS_WORK_LABEL: $rm_out"; exit 0
fi
report "cleared" "checks are passing; removed $NEEDS_WORK_LABEL"
