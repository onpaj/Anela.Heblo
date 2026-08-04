#!/usr/bin/env bash
# Remove needs-work and post the audit comment for one revised PR.
#
#   finish_revision.sh --pr N --summary-file PATH
#
# Only call this after the revision has been committed and pushed
# successfully — a failed revision must not look resolved.
set -uo pipefail

NEEDS_WORK_LABEL="needs-work"

PR=""; SUMMARY_FILE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --pr)            PR="$2"; shift 2 ;;
    --summary-file)  SUMMARY_FILE="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

report() {  # status, detail
  jq -n --argjson pr "${PR:-null}" --arg status "$1" --arg detail "$2" \
    '{pr: $pr, status: $status, detail: $detail}'
}

fail() { report "failed" "$1"; exit 1; }

[ -n "$PR" ] || { echo "--pr is required" >&2; exit 1; }
[ -n "$SUMMARY_FILE" ] && [ -f "$SUMMARY_FILE" ] || { echo "--summary-file must exist" >&2; exit 1; }

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
  url=$(git remote get-url origin 2>/dev/null) || fail "cannot detect repo: no origin remote"
  case "$url" in
    *github.com*) ;;
    *) fail "cannot detect repo: origin is not a github.com remote" ;;
  esac
  REPO="${url#*github.com[:/]}"
  REPO="${REPO%.git}"
  REPO="${REPO%/}"
  [ -n "$REPO" ] && [[ "$REPO" == */* ]] || fail "cannot detect repo: could not parse origin URL"
fi

# Post the audit comment before touching the label, so the trail exists even
# if the label edit below fails.
gh pr comment "$PR" --repo "$REPO" --body-file "$SUMMARY_FILE" \
  || fail "could not post revision summary comment"

gh pr edit "$PR" --repo "$REPO" --remove-label "$NEEDS_WORK_LABEL" \
  || fail "revision summary posted, but could not remove $NEEDS_WORK_LABEL label"

report "ok" "revision summary posted, $NEEDS_WORK_LABEL removed"
