#!/usr/bin/env bash
# Execute one already-decided action for one PR.
#
#   apply_verdict.sh --pr N --action merge|comment|needs-work \
#                    --review-file PATH [--issue N]
#
# This script does NOT decide anything — parse_verdict.py owns the thresholds.
# It executes the action it is handed, and reports what happened as JSON so the
# caller can continue to the next PR after a failure.
set -uo pipefail

MERGED_ISSUE_LABEL="agent-merged"
NEEDS_WORK_LABEL="needs-work"

PR=""; ACTION=""; REVIEW_FILE=""; ISSUE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --pr)          PR="$2"; shift 2 ;;
    --action)      ACTION="$2"; shift 2 ;;
    --review-file) REVIEW_FILE="$2"; shift 2 ;;
    --issue)       ISSUE="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

report() {  # status, detail
  # jq -n handles escaping (quotes, newlines, backslashes) correctly for any
  # $2 content — a hand-built printf JSON string breaks on multi-line `gh`
  # stderr, which is exactly what this function exists to report safely.
  jq -n --argjson pr "${PR:-null}" --arg action "$ACTION" \
    --arg status "$1" --arg detail "$2" \
    '{pr: $pr, action: $action, status: $status, detail: $detail}'
}

fail() { report "failed" "$1"; exit 1; }

[ -n "$PR" ] || { echo "--pr is required" >&2; exit 1; }
[ -n "$REVIEW_FILE" ] && [ -f "$REVIEW_FILE" ] || { echo "--review-file must exist" >&2; exit 1; }

case "$ACTION" in
  merge|comment|needs-work) ;;
  *) echo "unknown action: $ACTION" >&2; exit 1 ;;
esac

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
  # Same convention as .claude/skills/applicationinsightsscan/gh-api.sh's
  # detect_repo(): parse `origin` directly rather than relying on gh's own
  # remote-resolution heuristics.
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

# Always post the review first: it is the audit trail for whatever follows.
gh pr comment "$PR" --repo "$REPO" --body-file "$REVIEW_FILE" \
  || fail "could not post review comment"

case "$ACTION" in
  comment)
    report "ok" "review posted, left for a human"
    ;;

  needs-work)
    # Label may not exist yet; creating it is best-effort and idempotent.
    gh label create "$NEEDS_WORK_LABEL" --repo "$REPO" --color d93f0b \
      --description "Agent review found blocking problems" >/dev/null 2>&1 || true
    gh pr edit "$PR" --repo "$REPO" --add-label "$NEEDS_WORK_LABEL" \
      || fail "could not add $NEEDS_WORK_LABEL label"
    report "ok" "review posted, flagged $NEEDS_WORK_LABEL"
    ;;

  merge)
    if ! merge_err=$(gh pr merge "$PR" --repo "$REPO" --squash --delete-branch 2>&1); then
      # A PR that went unmergeable between listing and merging is not an error
      # in this run — master simply moved underneath it (most commonly because
      # an earlier PR in the same batch just merged ahead of it). Flag it
      # needs-work with a REJECT-verdict comment, the same pattern
      # hygiene-pr/update_and_wait.sh's report_and_flag_needs_work() uses, so
      # this PR stays discoverable by /rework-pr and counts toward its
      # revision-attempt cap exactly like any other auto-rejection — a silent
      # "skipped" would otherwise leave a now-conflicting PR invisible until
      # someone reruns this skill against it by hand.
      case "$merge_err" in
        *not\ mergeable*|*Merge\ conflict*|*conflict*)
          reject_file=$(mktemp)
          printf 'This PR became unmergeable before the merge completed — the default branch moved underneath it (commonly because an earlier PR in the same batch was just merged).\n\npr: %s\nscore: 0\nverdict: REJECT\nrisk: high\nreasons:\n  - merge conflict: %s\nconcerns: needs a rebase/merge against the current default branch, or /rework-pr\n' \
            "$PR" "$merge_err" > "$reject_file"
          gh label create "$NEEDS_WORK_LABEL" --repo "$REPO" --color d93f0b \
            --description "Agent review found blocking problems" >/dev/null 2>&1 || true
          if ! gh pr comment "$PR" --repo "$REPO" --body-file "$reject_file"; then
            rm -f "$reject_file"
            fail "became unmergeable, and could not post the needs-work comment: ${merge_err}"
          fi
          rm -f "$reject_file"
          if gh pr edit "$PR" --repo "$REPO" --add-label "$NEEDS_WORK_LABEL"; then
            report "needs-work" "became unmergeable before merge (default branch moved underneath it); flagged $NEEDS_WORK_LABEL"
          else
            report "needs-work" "became unmergeable before merge (default branch moved underneath it); could not add $NEEDS_WORK_LABEL label"
          fi
          exit 1
          ;;
        *)
          # jq -n in report() escapes this correctly now, so the raw
          # message can be passed through unmodified.
          fail "merge failed: ${merge_err}" ;;
      esac
    fi
    if [ -n "$ISSUE" ]; then
      # Label may not exist yet; creating it is best-effort and idempotent —
      # mirrors the needs-work path so a missing label can't turn a
      # successful merge into a reported failure.
      gh label create "$MERGED_ISSUE_LABEL" --repo "$REPO" --color 0e8a16 \
        --description "Auto-merged by /automerge-pr" >/dev/null 2>&1 || true
      if gh issue edit "$ISSUE" --repo "$REPO" --add-label "$MERGED_ISSUE_LABEL"; then
        report "ok" "squash-merged, branch deleted, issue #$ISSUE labelled"
      else
        # The merge already succeeded — a labelling failure must not be
        # reported as a failed PR, or the orchestrator would tell the user
        # a merge failed after master already moved.
        report "ok" "squash-merged, branch deleted, but could not label issue #$ISSUE"
      fi
    else
      report "ok" "squash-merged, branch deleted, no linked issue to label"
    fi
    ;;
esac
