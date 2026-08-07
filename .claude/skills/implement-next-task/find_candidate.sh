#!/usr/bin/env bash
# Find the next issue for the implementing stage: a SINGLE merged pool of
# every `agent-ready-for-dev` issue (always eligible -- a just-finished
# planning handoff is never "stale") plus every `agent-implementing` issue
# whose branch has had no commit in the staleness window (i.e. looks
# abandoned, not just slow) -- the oldest-by-createdAt across that
# combined eligible set wins, regardless of which label it carries.
#
# This is deliberately NOT the same strict-tiered pattern
# plan-next-issue/find_candidate.sh uses (fresh `agent` issues always beat
# a stale `agent-planning` reclaim there) -- the two situations differ.
# There, "reclaim" means "maybe still actively planning, don't wrongly
# interrupt it." Here, an `agent-implementing` issue past the staleness
# window genuinely has more work queued and needs to continue; preferring
# every fresh handoff over it unconditionally would starve a multi-task
# issue that's already mostly done behind an unbounded stream of brand-new
# issues that never finish either.
#
# Emits JSON: {"candidate": {number, title, createdAt, source}|null, "skipped": [...]}
# "source" is "fresh-handoff" or "stale-reclaim".
set -euo pipefail

READY_LABEL="agent-ready-for-dev"
IMPLEMENTING_LABEL="agent-implementing"
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

ready_json=$(gh issue list --repo "$REPO" --state open --label "$READY_LABEL" \
  --limit 100 --json number,title,createdAt)
implementing_json=$(gh issue list --repo "$REPO" --state open --label "$IMPLEMENTING_LABEL" \
  --limit 100 --json number,title,createdAt)

# Parse NOW_OVERRIDE or use current time. Handle both GNU date (Linux) and BSD date (macOS)
now_time="${NOW_OVERRIDE:-now}"
now_epoch=$(date -u -d "$now_time" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$now_time" +%s 2>/dev/null || date -u +%s)

# `agent-ready-for-dev` pool: every issue is immediately eligible, no
# recency check -- a fresh handoff from planning is never "stale".
ready_eligible=$(echo "$ready_json" | jq '[.[] | . + {source: "fresh-handoff"}]')

# `agent-implementing` pool: only eligible if the branch has had no commit
# in the last STALE_MINUTES -- an issue still being actively worked must
# not be preempted.
sorted_implementing_numbers=$(echo "$implementing_json" | jq -r 'sort_by(.createdAt) | .[].number')

implementing_eligible="[]"
skipped="[]"

for n in $sorted_implementing_numbers; do
  issue_obj=$(echo "$implementing_json" | jq --argjson n "$n" '.[] | select(.number == $n)')
  ref=$(git ls-remote --heads origin "feature/${n}-*" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
  if [ -z "$ref" ]; then
    skipped=$(echo "$skipped" | jq --argjson n "$n" \
      '. + [{number: $n, reason: "no branch found for a claimed implementing issue (unexpected)"}]')
    continue
  fi
  commit_data=$(gh api "repos/$REPO/commits/$ref" 2>/dev/null || echo "")
  # `gh api` prints its error body to stdout even on a non-zero exit, so a
  # failed call does NOT yield an empty string here -- it yields error
  # JSON, and `.commit.committer.date` on that is the literal string
  # "null", not empty. Treat "null" the same as empty.
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
      '. + [{number: $n, reason: "actively implementing, no commit age >'"$STALE_MINUTES"'min"}]')
    continue
  fi
  implementing_eligible=$(echo "$implementing_eligible" | jq --argjson obj "$issue_obj" \
    '. + [$obj + {source: "stale-reclaim"}]')
done

# Merge both eligible pools and pick the oldest by createdAt across the
# combined set -- unlike plan-next-issue/find_candidate.sh, this is NOT a
# strict tiered preference. See the header comment for why.
candidate=$(jq -n --argjson ready "$ready_eligible" --argjson impl "$implementing_eligible" \
  '($ready + $impl) | sort_by(.createdAt) | if length == 0 then null else .[0] end')

jq -n --argjson candidate "$candidate" --argjson skipped "$skipped" \
  '{candidate: $candidate, skipped: $skipped}'
