#!/usr/bin/env bash
# Bring one PR's branch current with its base branch and wait for CI to
# resolve. Read-only for every outcome except still-failing/conflict: those
# two mean the PR cannot be merged as-is regardless of who's asking, so this
# script flags it `needs-work` itself (via apply_verdict.sh, the same
# mechanism /automerge-pr uses for a code-review rejection) rather than
# leaving that to a caller that might never run — hygiene-pr/hygiene-all
# are meant to be usable standalone, and a status-only report with no
# durable label left a still-failing PR invisible to /rework-pr's own
# candidate search and to a human who wasn't staring at that one output.
#
#   update_and_wait.sh --pr N
#
# Emits JSON: {"pr": N, "status": "already-clean|fixed|still-failing|
#              conflict|pending-timeout|error", "detail": "..."}
# Always exits 0 once arguments validate — this script reports, it never
# fails the caller over a PR-hygiene outcome. (A missing/unknown argument
# still exits 1, matching the sibling scripts' convention.)
set -uo pipefail

NEEDS_WORK_SCRIPT=".claude/skills/automerge-pr/apply_verdict.sh"

POLL_INTERVAL_SECONDS="${HYGIENE_POLL_INTERVAL_SECONDS:-15}"
POLL_MAX_ATTEMPTS="${HYGIENE_POLL_MAX_ATTEMPTS:-40}"

PR=""
while [ $# -gt 0 ]; do
  case "$1" in
    --pr) PR="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

[ -n "$PR" ] || { echo "--pr is required" >&2; exit 1; }

report() {  # status, detail
  jq -n --argjson pr "$PR" --arg status "$1" --arg detail "$2" \
    '{pr: $pr, status: $status, detail: $detail}'
}

# Flags the PR needs-work and reports, for the two outcomes that mean it
# cannot be merged as-is (still-failing, conflict). Reuses apply_verdict.sh
# rather than reimplementing label/comment logic, so this stays the one
# place that owns it. The block below must keep a literal `verdict: REJECT`
# line — rework-pr/find_candidate.sh and list_candidates.sh count comment
# bodies matching that pattern toward the revision-attempt cap; breaking it
# would let a permanently broken build bounce between hygiene/automerge-pr
# and /rework-pr forever without ever hitting the cap. A labelling failure
# is noted in `detail`, not swallowed — but it never masks the underlying
# finding, which is real regardless of whether the flag landed.
report_and_flag_needs_work() {  # status, detail
  local status="$1" detail="$2" tmpfile verdict_out label_note=""
  tmpfile=$(mktemp)
  printf 'Hygiene check found this PR cannot be merged as-is.\n\npr: %s\nscore: 0\nverdict: REJECT\nrisk: high\nreasons:\n  - %s: %s\nconcerns: needs a human, or /rework-pr, to resolve\n' \
    "$PR" "$status" "$detail" > "$tmpfile"
  if ! verdict_out=$(GH_REPO="$REPO" "$NEEDS_WORK_SCRIPT" \
      --pr "$PR" --action needs-work --review-file "$tmpfile" 2>&1); then
    label_note=" (could not flag needs-work: $verdict_out)"
  fi
  rm -f "$tmpfile"
  report "$status" "$detail$label_note"
  exit 0
}

REPO="${GH_REPO:-}"
if [ -z "$REPO" ]; then
  # Same convention as automerge-pr/candidates.sh's detect_repo().
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

# Normalizes statusCheckRollup (a mix of CheckRun and legacy StatusContext
# objects) into one overall state: "success" | "failure" | "pending" | "none".
CI_STATE_FILTER='
  def check_state:
    if .__typename == "StatusContext" then
      (if .state == "SUCCESS" then "success"
       elif .state == "PENDING" then "pending"
       else "failure" end)
    else
      (if .status != "COMPLETED" then "pending"
       elif (.conclusion == "SUCCESS" or .conclusion == "NEUTRAL" or .conclusion == "SKIPPED") then "success"
       else "failure" end)
    end;
  def ci_state:
    if length == 0 then "none"
    else (map(check_state)) as $s
      | if ($s | any(. == "failure")) then "failure"
        elif ($s | any(. == "pending")) then "pending"
        else "success" end
    end;
'

# Sets the globals mergeable/merge_state/ci_state/base_ref/head_ref, or
# reports `error` and exits. Deliberately NOT read via `< <(read_state)`:
# process substitution runs in a subshell, so a failed `gh pr view` there
# is invisible to the caller and every variable silently comes back empty —
# which used to fall through into the poll loop and mis-report a GitHub API
# failure as a 10-minute `pending-timeout`.
read_state() {
  local raw rc line
  raw=$(gh pr view "$PR" --repo "$REPO" \
    --json mergeable,mergeStateStatus,statusCheckRollup,baseRefName,headRefName 2>&1)
  rc=$?
  if [ "$rc" -ne 0 ]; then
    report "error" "gh pr view failed (exit $rc): $raw"
    exit 0
  fi
  line=$(printf '%s' "$raw" | jq -r "$CI_STATE_FILTER"' [.mergeable, .mergeStateStatus, (.statusCheckRollup | ci_state), .baseRefName, .headRefName] | @tsv') || {
    report "error" "could not parse gh pr view output: $raw"
    exit 0
  }
  IFS=$'\t' read -r mergeable merge_state ci_state base_ref head_ref <<< "$line"
}

# How many commits the head branch is behind its base, straight from the
# compare API. This is the only staleness signal that works on a repo
# without branch protection: GitHub only ever sets mergeStateStatus=BEHIND
# when the base branch requires branches to be up to date before merging.
# Supplementary, so a failure here falls back to 0 (not behind) rather than
# erroring out — read_state() above already covers the primary failure mode.
behind_count() {  # base_ref, head_ref
  gh api "repos/$REPO/compare/$1...$2" --jq '.behind_by // 0' 2>/dev/null || echo 0
}

read_state

is_behind=false
[ "$merge_state" = "BEHIND" ] && is_behind=true
behind=$(behind_count "$base_ref" "$head_ref")
[ "$behind" -gt 0 ] 2>/dev/null && is_behind=true
is_conflicting=false
[ "$mergeable" = "CONFLICTING" ] && is_conflicting=true

did_update=false

if ! $is_behind && ! $is_conflicting; then
  case "$ci_state" in
    success|none)
      report "already-clean" "branch is current, checks are $ci_state"; exit 0 ;;
    failure)
      report_and_flag_needs_work "still-failing" "branch is current with base, but checks are failing" ;;
    pending)
      : # already current — fall through to the poll loop without updating
      ;;
  esac
else
  if ! update_err=$(gh pr update-branch "$PR" --repo "$REPO" 2>&1); then
    report_and_flag_needs_work "conflict" "gh pr update-branch failed: $update_err"
  fi
  did_update=true
fi

# Staleness is read once, above — the poll loop only re-reads CI state.
attempt=0
while [ "$attempt" -lt "$POLL_MAX_ATTEMPTS" ]; do
  read_state
  case "$ci_state" in
    success|none)
      if $did_update; then
        report "fixed" "branch updated, checks are $ci_state"
      else
        report "fixed" "branch was already current; checks finished as $ci_state"
      fi
      exit 0 ;;
    failure)
      report_and_flag_needs_work "still-failing" "branch is current with base, but checks are failing" ;;
  esac
  attempt=$((attempt + 1))
  [ "$attempt" -lt "$POLL_MAX_ATTEMPTS" ] && sleep "$POLL_INTERVAL_SECONDS"
done

report "pending-timeout" "checks still running after $POLL_MAX_ATTEMPTS polls"
