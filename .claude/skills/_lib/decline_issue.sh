#!/usr/bin/env bash
# Durably decline one issue: label it `agent-blocked` and leave a comment
# saying why.
#
#   decline_issue.sh --issue N --reason TEXT
#
# A pipeline worker that looks at a candidate and decides it cannot proceed
# (needs a human decision, ambiguous, out of scope...) must call this before
# it exits. Declining by merely stopping leaves no trace, so the next
# scheduled run picks the very same issue -- the oldest one -- and starves
# everything queued behind it. plan-next-task/ and implement-next-task/
# find_candidate.sh both skip `agent-blocked`, so labelling is what takes the
# issue out of rotation; a human removes the label to put it back.
#
# Idempotent: an issue that already carries the label gets no second comment.
#
# Emits JSON: {"issue": N, "status": "declined|already-blocked|error", "detail": "..."}
# Always exits 0 once arguments validate -- it reports, it never fails the
# caller. (A missing argument exits 1, matching the sibling scripts.)
set -uo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
LIB="$SCRIPT_DIR/gh_api.sh"

BLOCKED_LABEL="agent-blocked"
BLOCKED_COLOR="b60205"
BLOCKED_DESC="Declined by an unattended agent run; needs a human"

ISSUE=""; REASON=""
while [ $# -gt 0 ]; do
  case "$1" in
    --issue)  ISSUE="$2"; shift 2 ;;
    --reason) REASON="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done
[ -n "$ISSUE" ] || { echo "--issue is required" >&2; exit 1; }
[ -n "$REASON" ] || { echo "--reason is required" >&2; exit 1; }

report() {  # status, detail
  jq -n --argjson issue "$ISSUE" --arg status "$1" --arg detail "$2" \
    '{issue: $issue, status: $status, detail: $detail}'
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
  labels=$(GH_REPO="$REPO" "$LIB" issue-view "$ISSUE" 2>&1) \
    || { report "error" "could not read issue: $labels"; exit 0; }
  labels=$(echo "$labels" | jq -c '[(.labels // [])[].name]' 2>/dev/null || echo "[]")
else
  labels=$(gh issue view "$ISSUE" --repo "$REPO" --json labels --jq '[.labels[].name]' 2>&1) \
    || { report "error" "could not read issue: $labels"; exit 0; }
fi
if echo "$labels" | jq -e --arg b "$BLOCKED_LABEL" 'index($b)' >/dev/null 2>&1; then
  report "already-blocked" "issue already carries $BLOCKED_LABEL"
  exit 0
fi

# Label first: it is the part that stops the re-pick loop. The comment is
# the explanation and must not be able to prevent it.
if [ -n "${USE_GH_API:-}" ]; then
  GH_REPO="$REPO" "$LIB" label-create "$BLOCKED_LABEL" "$BLOCKED_COLOR" "$BLOCKED_DESC" >/dev/null 2>&1 || true
  label_err=$(GH_REPO="$REPO" "$LIB" issue-edit "$ISSUE" --add-label "$BLOCKED_LABEL" 2>&1) && label_ok=1 || label_ok=0
else
  gh label create "$BLOCKED_LABEL" --repo "$REPO" --color "$BLOCKED_COLOR" --description "$BLOCKED_DESC" >/dev/null 2>&1 || true
  label_err=$(gh issue edit "$ISSUE" --repo "$REPO" --add-label "$BLOCKED_LABEL" 2>&1) && label_ok=1 || label_ok=0
fi
if [ "$label_ok" -ne 1 ]; then
  report "error" "could not add $BLOCKED_LABEL: $label_err"
  exit 0
fi

body_file=$(mktemp)
printf 'An unattended agent run declined this issue and will not pick it up again until a human removes the `%s` label.\n\n**Reason:** %s\n' \
  "$BLOCKED_LABEL" "$REASON" > "$body_file"
if [ -n "${USE_GH_API:-}" ]; then
  comment_err=$(GH_REPO="$REPO" "$LIB" pr-comment "$ISSUE" "$body_file" 2>&1) && comment_ok=1 || comment_ok=0
else
  comment_err=$(gh issue comment "$ISSUE" --repo "$REPO" --body-file "$body_file" 2>&1) && comment_ok=1 || comment_ok=0
fi
rm -f "$body_file"

if [ "$comment_ok" -eq 1 ]; then
  report "declined" "labelled $BLOCKED_LABEL and commented"
else
  report "declined" "labelled $BLOCKED_LABEL, but could not post the comment: $comment_err"
fi
exit 0
