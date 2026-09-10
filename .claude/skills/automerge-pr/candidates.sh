#!/usr/bin/env bash
# List open PRs that are mechanically mergeable — `agent`-labelled ones by
# default, every open PR with --all-open.
#
#   candidates.sh [--include-conflicting] [--all-open]
#
# Emits JSON: {"candidates": [...], "skipped": [...], "truncated": N}
#
# Eligibility here is fact, not judgement: a draft or conflicted PR cannot be
# merged by anyone, so it is filtered out before any subagent is spawned.
#
# --include-conflicting keeps CONFLICTING PRs in the candidate list. That is
# /hygiene-all's mode: resolving a conflict is hygiene-pr's own job now, so
# the sweep feeding it has to see the conflicted PRs rather than filtering
# out exactly the ones it exists to fix. /automerge-all keeps the default —
# it reviews and merges, and can do neither until hygiene has fixed the
# conflict first.
#
# --all-open drops the `agent` label filter, so every open PR is a candidate
# regardless of who opened it or whether the pipeline labelled it. That is
# also /hygiene-all's mode: back-merging a base branch and reporting CI is
# not pipeline-specific work, and a PR nobody labelled is exactly the one
# that otherwise goes stale unnoticed — it does not even appear in
# `skipped`, because the label filter removes it before this script ever
# sees it. /automerge-all keeps the default: it reviews and merges
# autonomously, which is only ever delegated for `agent` PRs.
set -euo pipefail

INCLUDE_CONFLICTING=false
ALL_OPEN=false
while [ $# -gt 0 ]; do
  case "$1" in
    --include-conflicting) INCLUDE_CONFLICTING=true; shift ;;
    --all-open) ALL_OPEN=true; shift ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

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

# An empty LABEL_FILTER means "every open PR" — `gh pr list` takes that as an
# omitted --label flag, gh_api.sh's pr-list as an omitted LABEL_CSV argument.
LABEL_FILTER="$AGENT_LABEL"
$ALL_OPEN && LABEL_FILTER=""

if [ -n "${USE_GH_API:-}" ]; then
  GH_REPO="$REPO" "$LIB" pr-list open "$LABEL_FILTER"
else
  label_args=()
  [ -n "$LABEL_FILTER" ] && label_args=(--label "$LABEL_FILTER")
  gh pr list \
    --repo "$REPO" \
    --state open \
    "${label_args[@]+"${label_args[@]}"}" \
    --limit 100 \
    --json number,title,isDraft,mergeable,reviewDecision,headRefName,additions,deletions,changedFiles,body,labels,createdAt
fi \
| jq --argjson max "$MAX_CANDIDATES" --argjson include_conflicting "$INCLUDE_CONFLICTING" '
    # Must match NEEDS_WORK_LABEL / HUMAN_REQUIRED_LABEL in apply_verdict.sh —
    # the values are duplicated here (bash has no shared-constant mechanism
    # across these standalone scripts, the same tradeoff already made for
    # repo-detection logic), so keep both in sync if either label name ever
    # changes.
    def needs_work_label: "needs-work";
    def human_required_label: "human-required";

    def has_label($name): (.labels // [] | map(.name) | any(. == $name));

    # Split out so an included CONFLICTING PR still goes through them: a
    # conflicted PR that is also already needs-work or human-required is no
    # more a job for this sweep than a mergeable one in the same state.
    def review_reason:
      if .reviewDecision == "CHANGES_REQUESTED" then "CHANGES_REQUESTED"
      elif has_label(needs_work_label) then "needs-work (rejected by a previous run)"
      elif has_label(human_required_label) then "human-required (mid-confidence review already posted; awaiting a human)"
      else null end;

    def reason:
      if .isDraft then "draft"
      elif .mergeable == "CONFLICTING" then
        (if $include_conflicting then review_reason else "CONFLICTING (merge conflicts)" end)
      elif .mergeable == "UNKNOWN" then "UNKNOWN (mergeability not computed, retry)"
      elif .mergeable != "MERGEABLE" then "not mergeable: \(.mergeable)"
      else review_reason end;

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
