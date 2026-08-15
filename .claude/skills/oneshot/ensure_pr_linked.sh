#!/usr/bin/env bash
# Ensure a harness-opened PR is tied to its tracking issue.
#
# Guarantees, idempotently, that the pull request:
#   1. carries the `agent` label,
#   2. links the issue with a closing keyword (`Closes #<n>`) so merging the PR
#      auto-closes the issue, and
#   3. has a title in the defined format `#<issue>: <summary>`.
#
# Auto-repairs first (adds the label / injects a `Closes #<n>` line / normalizes
# the title), then verifies. Exits non-zero if it still cannot guarantee all three.
#
# Usage: ensure_pr_linked.sh <pr-url-or-number> <issue-number>
set -euo pipefail

PR="${1:-}"
ISSUE="${2:-}"

if [[ -z "$PR" || -z "$ISSUE" ]]; then
  echo "usage: ensure_pr_linked.sh <pr-url-or-number> <issue-number>" >&2
  exit 2
fi

if [[ ! "$ISSUE" =~ ^[0-9]+$ ]]; then
  echo "ERROR: issue number must be numeric, got: $ISSUE" >&2
  exit 2
fi

# GitHub closing keywords, immediately followed by `#<issue>` and a non-digit
# boundary (so `#1` does not match `#10`).
CLOSE_LINK="(clos(e|es|ed)|fix(es|ed)?|resolve(s|d)?) +#${ISSUE}([^0-9]|\$)"

# 1. Guarantee the `agent` label (idempotent add, then verify it landed).
gh pr edit "$PR" --add-label agent
if ! gh pr view "$PR" --json labels --jq '.labels[].name' | grep -qx agent; then
  echo "ERROR: agent label missing on $PR" >&2
  exit 1
fi

# 2. Guarantee a closing link to the issue (inject if absent, preserving body).
body=$(gh pr view "$PR" --json body --jq '.body')
if ! grep -qiE "$CLOSE_LINK" <<<"$body"; then
  printf -v new_body 'Closes #%s\n\n%s' "$ISSUE" "$body"
  gh pr edit "$PR" --body "$new_body"
  body=$(gh pr view "$PR" --json body --jq '.body')
  if ! grep -qiE "$CLOSE_LINK" <<<"$body"; then
    echo "ERROR: failed to add Closes #${ISSUE} to $PR" >&2
    exit 1
  fi
fi

# 3. Guarantee the title follows the defined format: "#<issue>: <summary>".
TITLE_RE="^#${ISSUE}: .+"
title=$(gh pr view "$PR" --json title --jq '.title')
if [[ ! "$title" =~ $TITLE_RE ]]; then
  # Strip any leading "#<anything>:" prefix (real number or un-substituted
  # placeholder like "#{issue_id}:"), trim, and keep the remainder as summary.
  summary=$(sed -E 's/^#[^:]*:[[:space:]]*//' <<<"$title")
  summary=$(sed -E 's/^[[:space:]]+|[[:space:]]+$//g' <<<"$summary")
  [[ -z "$summary" ]] && summary="implementation"
  gh pr edit "$PR" --title "#${ISSUE}: ${summary}"
  title=$(gh pr view "$PR" --json title --jq '.title')
  if [[ ! "$title" =~ $TITLE_RE ]]; then
    echo "ERROR: failed to set title format on $PR" >&2
    exit 1
  fi
fi

echo "PR $PR linked to issue #$ISSUE with agent label and title \"$title\"."
