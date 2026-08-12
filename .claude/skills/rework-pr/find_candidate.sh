#!/usr/bin/env bash
# Find the oldest open `needs-work` PR that hasn't hit the revision-attempt
# cap and isn't already claimed by an in-progress rework-pr run.
#
# Emits JSON: {"candidate": {...}|null, "skipped": [...]}
set -euo pipefail

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic here is unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

NEEDS_WORK_LABEL="needs-work"
AGENT_WIP_LABEL="agent-wip"
MAX_REVISION_ATTEMPTS=3
# /automerge-pr only ever labels PRs that already carry `agent` (see
# automerge-pr/candidates.sh's own `--label agent` filter), so this keeps
# eligibility here to "PRs /automerge-pr itself rejected" rather than any
# needs-work-labelled PR from any source (human-labelled, a fork PR, etc.).
AGENT_LABEL="agent"

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

candidate="null"
skipped="[]"

for n in $sorted_numbers; do
  pr_obj=$(echo "$prs_json" | jq --argjson n "$n" '.[] | select(.number == $n)')
  is_draft=$(echo "$pr_obj" | jq -r '.isDraft')
  mergeable=$(echo "$pr_obj" | jq -r '.mergeable')
  has_needs_work=$(echo "$pr_obj" | jq --arg l "$NEEDS_WORK_LABEL" '[.labels[]?.name] | any(. == $l)')
  has_agent=$(echo "$pr_obj" | jq --arg l "$AGENT_LABEL" '[.labels[]?.name] | any(. == $l)')
  has_agent_wip=$(echo "$pr_obj" | jq --arg l "$AGENT_WIP_LABEL" '[.labels[]?.name] | any(. == $l)')

  # Draft/unresolved-mergeability PRs are out of scope. CONFLICTING is no
  # longer skipped here — rework-pr's own merge-and-resolve step (SKILL.md
  # step 5) is what handles a genuinely conflicting PR.
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
  # gh pr list --label filters via GitHub's search index, which can lag
  # behind live label state for a short window after a label change. The
  # live .labels field (already in hand from the query above) is the
  # source of truth.
  if [ "$has_needs_work" != "true" ] || [ "$has_agent" != "true" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "stale search match (no longer carries needs-work+agent live)"}]')
    continue
  fi

  # --paginate: GitHub defaults to 30 comments/page, oldest first, so on any
  # PR with more than 30 comments the most recent `verdict: REJECT` blocks
  # would otherwise sit on pages that are never fetched, undercounting
  # attempts and letting the cap never trip.
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

  candidate=$(echo "$pr_obj" | jq --argjson a "$attempts" '
    def linked_issue:
      ([(.body // "") | scan("[Cc]loses #([0-9]+)")]) as $matches
      | if ($matches | length) == 0 then null else ($matches[0][0] | tonumber) end;
    {number, title, headRefName, attempts: $a, linkedIssue: linked_issue}
  ')
  break
done

jq -n --argjson candidate "$candidate" --argjson skipped "$skipped" \
  '{candidate: $candidate, skipped: $skipped}'
