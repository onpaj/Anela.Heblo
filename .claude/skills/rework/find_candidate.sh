#!/usr/bin/env bash
# Find the oldest open `needs-work` PR that hasn't hit the revision-attempt
# cap.
#
# Emits JSON: {"candidate": {...}|null, "skipped": [...]}
set -euo pipefail

NEEDS_WORK_LABEL="needs-work"
MAX_REVISION_ATTEMPTS=3
# /automerge only ever labels PRs that already carry `agent` (see
# automerge/candidates.sh's own `--label agent` filter), so this keeps
# eligibility here to "PRs /automerge itself rejected" rather than any
# needs-work-labelled PR from any source (human-labelled, a fork PR, etc.).
AGENT_LABEL="agent"

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
  # Same convention as .claude/skills/applicationinsightsscan/gh-api.sh's
  # detect_repo() and automerge/candidates.sh: parse `origin` directly.
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

prs_json=$(gh pr list \
  --repo "$REPO" \
  --state open \
  --label "$NEEDS_WORK_LABEL" \
  --label "$AGENT_LABEL" \
  --limit 100 \
  --json number,title,createdAt,headRefName,body,isDraft,mergeable)

sorted_numbers=$(echo "$prs_json" | jq -r 'sort_by(.createdAt) | .[].number')

candidate="null"
skipped="[]"

for n in $sorted_numbers; do
  pr_obj=$(echo "$prs_json" | jq --argjson n "$n" '.[] | select(.number == $n)')
  is_draft=$(echo "$pr_obj" | jq -r '.isDraft')
  mergeable=$(echo "$pr_obj" | jq -r '.mergeable')

  # Draft/conflicted/unresolved-mergeability PRs are out of scope (see the
  # design spec's Scope section), mirroring automerge/candidates.sh's own
  # draft/CONFLICTING/UNKNOWN filter.
  if [ "$is_draft" = "true" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "draft"}]')
    continue
  fi
  if [ "$mergeable" = "CONFLICTING" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "CONFLICTING (merge conflicts)"}]')
    continue
  fi
  if [ "$mergeable" = "UNKNOWN" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "UNKNOWN (mergeability not computed, retry)"}]')
    continue
  fi

  # --paginate: GitHub defaults to 30 comments/page, oldest first, so on any
  # PR with more than 30 comments the most recent `verdict: REJECT` blocks
  # would otherwise sit on pages that are never fetched, undercounting
  # attempts and letting the cap never trip.
  comments_json=$(gh api --paginate "repos/$REPO/issues/$n/comments")
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
