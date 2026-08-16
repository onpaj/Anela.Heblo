#!/usr/bin/env bash
# List open `agent` PRs that are mechanically mergeable.
#
# Emits JSON: {"candidates": [...], "skipped": [...], "truncated": N}
#
# Eligibility here is fact, not judgement: a draft or conflicted PR cannot be
# merged by anyone, so it is filtered out before any subagent is spawned.
set -euo pipefail

# When USE_GH_API is set, the `gh pr list` call below routes through the
# shared curl+REST library instead — for environments where the `gh` CLI
# itself is not permitted. See .claude/skills/_lib/gh_api.sh for the
# transport layer; the eligibility logic below is unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

AGENT_LABEL="agent"
MAX_CANDIDATES=20

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
  # Same convention as .claude/skills/applicationinsightsscan/gh-api.sh's
  # detect_repo(): parse `origin` directly rather than relying on gh's own
  # remote-resolution heuristics.
  url=$(git remote get-url origin 2>/dev/null) || { echo "cannot detect repo: no origin remote" >&2; exit 1; }
  case "$url" in
    *github.com*) ;;
    *) echo "cannot detect repo: origin is not a github.com remote" >&2; exit 1 ;;
  esac
  REPO="${url#*github.com[:/]}"
  REPO="${REPO%.git}"
  REPO="${REPO%/}"
  if [ -z "$REPO" ] || [[ "$REPO" != */* ]]; then
    echo "cannot detect repo: could not parse origin URL" >&2; exit 1
  fi
fi

if [ -n "${USE_GH_API:-}" ]; then
  GH_REPO="$REPO" "$LIB" pr-list open "$AGENT_LABEL"
else
  gh pr list \
    --repo "$REPO" \
    --state open \
    --label "$AGENT_LABEL" \
    --limit 100 \
    --json number,title,isDraft,mergeable,reviewDecision,headRefName,additions,deletions,changedFiles,body,labels,createdAt
fi \
| jq --argjson max "$MAX_CANDIDATES" '
    # Must match NEEDS_WORK_LABEL / HUMAN_REQUIRED_LABEL in apply_verdict.sh —
    # the values are duplicated here (bash has no shared-constant mechanism
    # across these standalone scripts, the same tradeoff already made for
    # repo-detection logic), so keep both in sync if either label name ever
    # changes.
    def needs_work_label: "needs-work";
    def human_required_label: "human-required";

    def has_label($name): (.labels // [] | map(.name) | any(. == $name));

    def reason:
      if .isDraft then "draft"
      elif .mergeable == "CONFLICTING" then "CONFLICTING (merge conflicts)"
      elif .mergeable == "UNKNOWN" then "UNKNOWN (mergeability not computed, retry)"
      elif .mergeable != "MERGEABLE" then "not mergeable: \(.mergeable)"
      elif .reviewDecision == "CHANGES_REQUESTED" then "CHANGES_REQUESTED"
      elif has_label(needs_work_label) then "needs-work (rejected by a previous run)"
      elif has_label(human_required_label) then "human-required (mid-confidence review already posted; awaiting a human)"
      else null end;

    def linked_issue:
      ([(.body // "") | scan("[Cc]loses #([0-9]+)")]) as $matches
      | if ($matches | length) == 0 then null else ($matches[0][0] | tonumber) end;

    (map(select(reason == null))          | sort_by(.number)) as $ok
  | (map(select(reason != null))
      | map({number, reason: reason})     | sort_by(.number)) as $skipped
  | {
      candidates: ($ok[:$max] | map({number, title, additions, changedFiles, linkedIssue: linked_issue, createdAt})),
      skipped: $skipped,
      truncated: (($ok | length) - $max | if . < 0 then 0 else . end)
    }
  '
