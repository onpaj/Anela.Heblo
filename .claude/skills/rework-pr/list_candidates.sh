#!/usr/bin/env bash
# List every open `needs-work` PR eligible for /rework-pr — same
# eligibility rules as find_candidate.sh, but returns all of them
# (oldest first) instead of stopping at the first.
#
# Emits JSON: {"candidates": [...], "skipped": [...]}
set -euo pipefail

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic here is unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

NEEDS_WORK_LABEL="needs-work"
AGENT_WIP_LABEL="agent-wip"
MAX_REVISION_ATTEMPTS=3
AGENT_LABEL="agent"
# Same bound and reasoning as automerge-pr/candidates.sh's MAX_CANDIDATES:
# rework-all spawns one subagent per candidate, so this caps how many run
# in parallel in one invocation.
MAX_CANDIDATES=20

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
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
  prs_json=$(GH_REPO="$REPO" "$LIB" pr-list open "${NEEDS_WORK_LABEL},${AGENT_LABEL}")
else
  prs_json=$(gh pr list \
    --repo "$REPO" \
    --state open \
    --label "$NEEDS_WORK_LABEL" \
    --label "$AGENT_LABEL" \
    --limit 100 \
    --json number,title,createdAt,headRefName,body,isDraft,mergeable,labels)
fi

sorted_numbers=$(echo "$prs_json" | jq -r 'sort_by(.createdAt) | .[].number')

candidates="[]"
skipped="[]"
candidate_count=0
truncated=0

for n in $sorted_numbers; do
  pr_obj=$(echo "$prs_json" | jq --argjson n "$n" '.[] | select(.number == $n)')
  is_draft=$(echo "$pr_obj" | jq -r '.isDraft')
  mergeable=$(echo "$pr_obj" | jq -r '.mergeable')
  has_needs_work=$(echo "$pr_obj" | jq --arg l "$NEEDS_WORK_LABEL" '[.labels[]?.name] | any(. == $l)')
  has_agent=$(echo "$pr_obj" | jq --arg l "$AGENT_LABEL" '[.labels[]?.name] | any(. == $l)')
  has_agent_wip=$(echo "$pr_obj" | jq --arg l "$AGENT_WIP_LABEL" '[.labels[]?.name] | any(. == $l)')

  if [ "$is_draft" = "true" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "draft"}]')
    continue
  fi
  if [ "$mergeable" = "UNKNOWN" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "UNKNOWN (mergeability not computed, retry)"}]')
    continue
  fi
  if [ "$has_agent_wip" = "true" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "claimed by an in-progress rework-pr run"}]')
    continue
  fi
  if [ "$has_needs_work" != "true" ] || [ "$has_agent" != "true" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "stale search match (no longer carries needs-work+agent live)"}]')
    continue
  fi

  # Past the cap: stop spending an API call on this PR's comment history —
  # it just counts toward `truncated`, same semantics as candidates.sh's
  # own slice-then-count.
  if [ "$candidate_count" -ge "$MAX_CANDIDATES" ]; then
    truncated=$((truncated + 1))
    continue
  fi

  if [ -n "${USE_GH_API:-}" ]; then
    comments_json=$(GH_REPO="$REPO" "$LIB" paginate "repos/$REPO/issues/$n/comments")
  else
    comments_json=$(gh api --paginate "repos/$REPO/issues/$n/comments")
  fi
  attempts=$(echo "$comments_json" \
    | jq '[.[].body // "" | select(test("verdict:\\s*REJECT"))] | length')

  if [ "$attempts" -ge "$MAX_REVISION_ATTEMPTS" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" --argjson a "$attempts" \
      '. + [{number: $n, reason: "revision cap reached (\($a) attempts)"}]')
    continue
  fi

  entry=$(echo "$pr_obj" | jq --argjson a "$attempts" '
    def linked_issue:
      ([(.body // "") | scan("[Cc]loses #([0-9]+)")]) as $matches
      | if ($matches | length) == 0 then null else ($matches[0][0] | tonumber) end;
    {number, title, headRefName, createdAt, attempts: $a, linkedIssue: linked_issue}
  ')
  candidates=$(echo "$candidates" | jq --argjson e "$entry" '. + [$e]')
  candidate_count=$((candidate_count + 1))
done

jq -n --argjson candidates "$candidates" --argjson skipped "$skipped" --argjson truncated "$truncated" \
  '{candidates: $candidates, skipped: $skipped, truncated: $truncated}'
