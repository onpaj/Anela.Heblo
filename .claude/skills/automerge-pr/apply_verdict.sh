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

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic here is unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

pr_comment_file() {  # pr, file
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-comment "$1" "$2"
  else
    gh pr comment "$1" --repo "$REPO" --body-file "$2"
  fi
}

label_create() {  # name, color, description
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" label-create "$1" "$2" "$3" >/dev/null 2>&1 || true
  else
    gh label create "$1" --repo "$REPO" --color "$2" --description "$3" >/dev/null 2>&1 || true
  fi
}

pr_add_label() {  # pr, label
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-edit "$1" --add-label "$2"
  else
    gh pr edit "$1" --repo "$REPO" --add-label "$2"
  fi
}

issue_add_label() {  # issue, label
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" issue-edit "$1" --add-label "$2"
  else
    gh issue edit "$1" --repo "$REPO" --add-label "$2"
  fi
}

pr_merge_squash_delete() {  # pr
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-merge "$1" --squash --delete-branch 2>&1
  else
    gh pr merge "$1" --repo "$REPO" --squash --delete-branch 2>&1
  fi
}

MERGED_ISSUE_LABEL="agent-merged"
NEEDS_WORK_LABEL="needs-work"
HUMAN_REQUIRED_LABEL="human-required"

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
pr_comment_file "$PR" "$REVIEW_FILE" \
  || fail "could not post review comment"

case "$ACTION" in
  comment)
    # A mid-band score is not "try again next run" — without a label, every
    # future sweep re-reviews and re-comments on this same PR forever. Flag
    # it so candidates.sh excludes it until a human clears the label.
    label_create "$HUMAN_REQUIRED_LABEL" fbca04 "Agent review is unsure; needs a human decision"
    if pr_add_label "$PR" "$HUMAN_REQUIRED_LABEL"; then
      report "ok" "review posted, flagged $HUMAN_REQUIRED_LABEL"
    else
      report "ok" "review posted, but could not add $HUMAN_REQUIRED_LABEL label"
    fi
    ;;

  needs-work)
    # Label may not exist yet; creating it is best-effort and idempotent.
    label_create "$NEEDS_WORK_LABEL" d93f0b "Agent review found blocking problems"
    pr_add_label "$PR" "$NEEDS_WORK_LABEL" \
      || fail "could not add $NEEDS_WORK_LABEL label"
    report "ok" "review posted, flagged $NEEDS_WORK_LABEL"
    ;;

  merge)
    if ! merge_err=$(pr_merge_squash_delete "$PR"); then
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
          label_create "$NEEDS_WORK_LABEL" d93f0b "Agent review found blocking problems"
          if ! pr_comment_file "$PR" "$reject_file"; then
            rm -f "$reject_file"
            fail "became unmergeable, and could not post the needs-work comment: ${merge_err}"
          fi
          rm -f "$reject_file"
          if pr_add_label "$PR" "$NEEDS_WORK_LABEL"; then
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
      label_create "$MERGED_ISSUE_LABEL" 0e8a16 "Auto-merged by /automerge-pr"
      if issue_add_label "$ISSUE" "$MERGED_ISSUE_LABEL"; then
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
