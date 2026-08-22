#!/usr/bin/env bash
# Flag one PR `needs-work` with the standard hygiene rejection comment.
#
#   flag_needs_work.sh --pr N --status STATUS --detail TEXT
#
# Two hygiene callers need an identical durable trail — update_and_wait.sh
# for a PR whose CI is failing, and resolve_conflict.sh for a merge conflict
# the run could not resolve — so the comment block lives here once rather
# than being copied into both. Both go through automerge-pr/apply_verdict.sh,
# the same mechanism /automerge-pr uses for a code-review rejection, so a
# hygiene rejection is indistinguishable from a review one to /rework-pr.
#
# The block below must keep a literal `verdict: REJECT` line —
# rework-pr/find_candidate.sh and list_candidates.sh count comment bodies
# matching that pattern toward the revision-attempt cap; breaking it would
# let a permanently broken PR bounce between hygiene/automerge-pr and
# /rework-pr forever without ever hitting the cap.
#
# Silent and exit 0 on success. On failure, prints apply_verdict.sh's output
# and exits 1 — callers fold that into their own `detail` rather than
# swallowing it, because the underlying finding is real either way.
#
# $GH_REPO must already be set by the caller (every hygiene script resolves
# the repo up front).
set -uo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
APPLY_VERDICT="$SCRIPT_DIR/../automerge-pr/apply_verdict.sh"

PR=""; STATUS=""; DETAIL=""
while [ $# -gt 0 ]; do
  case "$1" in
    --pr)     PR="$2"; shift 2 ;;
    --status) STATUS="$2"; shift 2 ;;
    --detail) DETAIL="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

[ -n "$PR" ] || { echo "--pr is required" >&2; exit 1; }
[ -n "$STATUS" ] || { echo "--status is required" >&2; exit 1; }

tmpfile=$(mktemp)
printf 'Hygiene check found this PR cannot be merged as-is.\n\npr: %s\nscore: 0\nverdict: REJECT\nrisk: high\nreasons:\n  - %s: %s\nconcerns: needs a human, or /rework-pr, to resolve\n' \
  "$PR" "$STATUS" "$DETAIL" > "$tmpfile"

if out=$("$APPLY_VERDICT" --pr "$PR" --action needs-work --review-file "$tmpfile" 2>&1); then
  rm -f "$tmpfile"
  exit 0
fi

rm -f "$tmpfile"
echo "$out"
exit 1
