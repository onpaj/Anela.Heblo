#!/usr/bin/env bash
#
# pr-scan.sh — pick the single PR the pr-autoabsorb routine should work on next.
#
# Deterministic selector for the routine: lists open PRs, keeps only
# agent/bot-authored ones (head branch `claude/*` OR the `agent` label), drops
# anything previously flagged `absorb-blocked`, keeps those that are BROKEN
# (merge conflicts against base, OR a failed CI check), and prints the OLDEST
# such PR's number. Prints nothing when there is no work — a valid, common
# outcome.
#
# GitHub access is via the `gh` CLI (installed in the routine environment; auth
# from GH_TOKEN / GIT_PAT). jq is required for the selection logic.
#
# Usage:
#   docs/routines/pr-autoabsorb/pr-scan.sh           # print the chosen PR number (or nothing)
#   docs/routines/pr-autoabsorb/pr-scan.sh --all     # debug: table of every eligible PR + reason
#
set -euo pipefail

REPO="${GH_REPO:-onpaj/Anela.Heblo}"

err() { echo "Error: $*" >&2; exit 1; }
command -v gh >/dev/null || err "gh CLI not found — the routine environment must install it (see README)."
command -v jq >/dev/null || err "jq is required."

# Make gh non-interactive and let it pick up a PAT if GH_TOKEN isn't already set.
export GH_PROMPT_DISABLED=1
[[ -z "${GH_TOKEN:-}" && -n "${GIT_PAT:-}" ]] && export GH_TOKEN="$GIT_PAT"

# A check is "failed" if its conclusion/state names a real failure (not pending,
# success, skipped, neutral, or action_required).
FAIL_RE='FAIL|ERROR|TIMED_OUT|CANCEL|STARTUP_FAILURE'

raw="$(gh pr list --repo "$REPO" --state open --limit 100 \
  --json number,headRefName,labels,mergeable,createdAt,isDraft,statusCheckRollup)"

# Annotate each eligible PR with the reason(s) it needs attention.
eligible="$(jq -c --arg fail "$FAIL_RE" '
  def is_agent: (.headRefName | startswith("claude/"))
                or (any(.labels[]?; .name == "agent"));
  def is_blocked: any(.labels[]?; .name == "absorb-blocked");
  def has_conflict: .mergeable == "CONFLICTING";
  def has_failed_ci:
    any(.statusCheckRollup[]?;
        ((.conclusion // .state // "") | ascii_upcase | test($fail)));
  map(select(.isDraft | not))
  | map(select(is_agent and (is_blocked | not)))
  | map(select(has_conflict or has_failed_ci))
  | map({number, headRefName, createdAt,
         reason: ([ (if has_conflict then "conflict" else empty end),
                    (if has_failed_ci then "failed-ci" else empty end) ] | join("+"))})
  | sort_by(.createdAt)
' <<<"$raw")"

if [[ "${1:-}" == "--all" ]]; then
  jq -r '
    if length == 0 then "_(no eligible PRs)_"
    else (["PR","branch","reason","created"], ["--","--","--","--"]),
         (.[] | [("#" + (.number|tostring)), .headRefName, .reason, .createdAt])
    end | @tsv' <<<"$eligible" \
    | { command -v column >/dev/null && column -t -s $'\t' || cat; }
  exit 0
fi

# Default: just the oldest eligible PR number, or nothing.
jq -r '.[0].number // empty' <<<"$eligible"
