#!/usr/bin/env bash
# Find the next issue for the planning stage: the oldest unclaimed `agent`
# issue, or (only if none exist) the oldest `agent-planning` issue whose
# claim looks abandoned (its branch has had no commit in the staleness
# window).
#
# Emits JSON: {"candidate": {number, title, createdAt, source}|null, "skipped": [...]}
# "source" is "fresh" (caller must claim_issue.sh next) or "stale-reclaim"
# (already claimed, caller attaches the worktree directly).
set -euo pipefail

AGENT_LABEL="agent"
PLANNING_LABEL="agent-planning"
STALE_MINUTES="${STALE_MINUTES:-10}"
[[ "$STALE_MINUTES" =~ ^[0-9]+$ ]] || { echo "ERROR: STALE_MINUTES must be a non-negative integer, got: $STALE_MINUTES" >&2; exit 1; }

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

agent_json=$(gh issue list --repo "$REPO" --state open --label "$AGENT_LABEL" \
  --limit 100 --json number,title,createdAt)

# Walk the `agent`-labeled pool oldest-to-newest and skip any issue that
# already has a feature/{n}-* branch on origin -- this happens when
# claim_issue.sh's label swap failed after the claim ref was already
# created (e.g. the target label didn't exist in the repo yet), leaving
# the issue claimed (branch exists) but still labelled `agent`. Without
# this walk, that one issue would be returned as "fresh" on every
# invocation, `claim_issue.sh` would exit 3 ("already claimed") every
# time, and planning would deadlock on it forever with no other issue ever
# getting planned. Mirrors the self-healing walk `chopchop/SKILL.md` step 2
# already does for its own candidate pool.
sorted_agent_numbers=$(echo "$agent_json" | jq -r 'sort_by(.createdAt) | .[].number')

fresh_candidate="null"
fresh_skipped="[]"

for n in $sorted_agent_numbers; do
  issue_obj=$(echo "$agent_json" | jq --argjson n "$n" '.[] | select(.number == $n)')
  ref=$(git ls-remote --heads origin "feature/${n}-*" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
  if [ -n "$ref" ]; then
    fresh_skipped=$(echo "$fresh_skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: ("already has a feature/" + ($n | tostring) + "- branch (claim likely did not fully complete)")}]')
    continue
  fi
  fresh_candidate=$(echo "$issue_obj" | jq '. + {source: "fresh"}')
  break
done

if [ "$fresh_candidate" != "null" ]; then
  jq -n --argjson candidate "$fresh_candidate" --argjson skipped "$fresh_skipped" \
    '{candidate: $candidate, skipped: $skipped}'
  exit 0
fi

# The whole `agent`-labeled pool is exhausted (all skipped or none exist)
# -- look for a stale `agent-planning` claim to reclaim.
planning_json=$(gh issue list --repo "$REPO" --state open --label "$PLANNING_LABEL" \
  --limit 100 --json number,title,createdAt)

# Parse NOW_OVERRIDE or use current time. Handle both GNU date (Linux) and BSD date (macOS)
now_time="${NOW_OVERRIDE:-now}"
now_epoch=$(date -u -d "$now_time" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$now_time" +%s 2>/dev/null || date -u +%s)

sorted_numbers=$(echo "$planning_json" | jq -r 'sort_by(.createdAt) | .[].number')

candidate="null"
skipped="[]"

for n in $sorted_numbers; do
  issue_obj=$(echo "$planning_json" | jq --argjson n "$n" '.[] | select(.number == $n)')
  ref=$(git ls-remote --heads origin "feature/${n}-*" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
  if [ -z "$ref" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "claimed but no branch found yet (in progress)"}]')
    continue
  fi
  commit_data=$(gh api "repos/$REPO/commits/$ref" 2>/dev/null || echo "")
  # `gh api` prints its error body to stdout even on a non-zero exit (e.g.
  # a 404 or a transient 5xx), so a failed call does NOT yield an empty
  # string here -- it yields error JSON like {"message":"Not Found"}, and
  # `.commit.committer.date` on that is the literal string "null", not
  # empty. Treat "null" the same as empty so this doesn't fall through to
  # the date parse below with garbage input.
  commit_date=$(echo "$commit_data" | jq -r '.commit.committer.date' 2>/dev/null || echo "")
  if [ -z "$commit_date" ] || [ "$commit_date" = "null" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "could not read branch commit date"}]')
    continue
  fi
  # Parse commit date, handling both GNU date (Linux) and BSD date (macOS).
  # A final fallback to "" (rather than letting `date` fail under set -e)
  # ensures a genuinely unparseable date skips this candidate instead of
  # killing the whole script.
  commit_epoch=$(date -u -d "$commit_date" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$commit_date" +%s 2>/dev/null || echo "")
  if [ -z "$commit_epoch" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "could not parse commit date"}]')
    continue
  fi
  age_minutes=$(( (now_epoch - commit_epoch) / 60 ))
  if [ "$age_minutes" -lt "$STALE_MINUTES" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "planning in progress, no commit age >'"$STALE_MINUTES"'min"}]')
    continue
  fi
  candidate=$(echo "$issue_obj" | jq '. + {source: "stale-reclaim"}')
  break
done

jq -n --argjson candidate "$candidate" --argjson skipped "$skipped" --argjson fresh_skipped "$fresh_skipped" \
  '{candidate: $candidate, skipped: ($fresh_skipped + $skipped)}'
