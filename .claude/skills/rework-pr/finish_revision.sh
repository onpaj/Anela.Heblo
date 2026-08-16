#!/usr/bin/env bash
# Release the agent-wip claim, post the audit comment, and remove
# needs-work, for one revised PR — in that order.
#
#   finish_revision.sh --pr N --summary-file PATH
#
# Only call this after the revision has been committed and pushed
# successfully — a failed revision must not look resolved.
set -uo pipefail

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic here is unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

pr_remove_label() {  # pr, label
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-edit "$1" --remove-label "$2"
  else
    gh pr edit "$1" --repo "$REPO" --remove-label "$2"
  fi
}

pr_comment_file() {  # pr, file
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-comment "$1" "$2"
  else
    gh pr comment "$1" --repo "$REPO" --body-file "$2"
  fi
}

NEEDS_WORK_LABEL="needs-work"
AGENT_WIP_LABEL="agent-wip"

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

# Release the claim FIRST. The claim and the needs-work label are
# independent: if a later step fails, this script exits non-zero and nothing
# else releases $AGENT_WIP_LABEL — a PR left carrying it is invisible to
# every future find_candidate.sh/list_candidates.sh run, permanently. Losing
# the audit comment is recoverable; leaking the claim is not.
pr_remove_label "$PR" "$AGENT_WIP_LABEL" \
  || fail "could not remove $AGENT_WIP_LABEL label"

pr_comment_file "$PR" "$SUMMARY_FILE" \
  || fail "$AGENT_WIP_LABEL released, but could not post revision summary comment"

pr_remove_label "$PR" "$NEEDS_WORK_LABEL" \
  || fail "$AGENT_WIP_LABEL released and revision summary posted, but could not remove $NEEDS_WORK_LABEL label"

report "ok" "$AGENT_WIP_LABEL released, revision summary posted, $NEEDS_WORK_LABEL removed"
