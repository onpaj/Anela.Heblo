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
#   update_and_wait.sh --pr N [--force]
#
# Emits JSON: {"pr": N, "status": "already-clean|fixed|still-failing|
#              conflict|ci-running|pending-timeout|error", "detail": "..."}
# Always exits 0 once arguments validate — this script reports, it never
# fails the caller over a PR-hygiene outcome. (A missing/unknown argument
# still exits 1, matching the sibling scripts' convention.)
#
# `ci-running` vs `pending-timeout`: if CI is already mid-flight on read —
# from some earlier push this run didn't cause — this script does not touch
# the PR at all (no update-branch call) and reports `ci-running` immediately;
# forcing an update-branch here would cancel a build already in progress for
# no reason, and there's no telling how much longer it has left to run. A
# caller should just retry on its next sweep. `pending-timeout` is different:
# it only happens after THIS run performed an update-branch (or found the
# branch already current) and then polled the CI it's responsible for past
# POLL_MAX_ATTEMPTS.
#
# --force does two things, both of them "back-merge/wait even though nothing
# requires it": it overrides the `ci-running` short-circuit above, and it
# treats a PR that is merely behind its base — mergeable, just not current —
# as needing an update. It still does NOT force a `gh pr update-branch` call
# when there is nothing at all to merge: a PR already level with its base
# just polls/confirms whatever CI is doing.
set -uo pipefail

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic here is unchanged either way.
LIB=".claude/skills/_lib/gh_api.sh"

NEEDS_WORK_SCRIPT=".claude/skills/automerge-pr/apply_verdict.sh"

POLL_INTERVAL_SECONDS="${HYGIENE_POLL_INTERVAL_SECONDS:-15}"
POLL_MAX_ATTEMPTS="${HYGIENE_POLL_MAX_ATTEMPTS:-40}"
# How many polls to give GitHub to create the checks for a commit this run
# just pushed, before concluding the repo has no PR CI at all. See the poll
# loop for why an empty rollup right after a push isn't the same as a green
# one.
NO_CHECKS_GRACE_ATTEMPTS="${HYGIENE_NO_CHECKS_GRACE_ATTEMPTS:-4}"

PR=""
FORCE=false
while [ $# -gt 0 ]; do
  case "$1" in
    --pr) PR="$2"; shift 2 ;;
    --force) FORCE=true; shift ;;
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
  if [ -n "${USE_GH_API:-}" ]; then
    raw=$(GH_REPO="$REPO" "$LIB" pr-view "$PR" statusCheckRollup 2>&1)
  else
    raw=$(gh pr view "$PR" --repo "$REPO" \
      --json mergeable,mergeStateStatus,statusCheckRollup,baseRefName,headRefName 2>&1)
  fi
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
# compare API — consulted only under --force. GitHub sets
# mergeStateStatus=BEHIND exactly when being behind actually blocks the merge
# (the base branch requires branches to be up to date), so anywhere else a
# nonzero behind_by describes a PR that merges perfectly well as-is.
# Back-merging it anyway buys nothing and costs a fresh CI run — whose
# in-flight state then makes the next sweep skip the PR as `ci-running`,
# which is how a whole backlog ended up permanently skipped.
# Supplementary, so a failure here falls back to 0 (not behind) rather than
# erroring out — read_state() above already covers the primary failure mode.
behind_count() {  # base_ref, head_ref
  if [ -n "${USE_GH_API:-}" ]; then
    GH_REPO="$REPO" "$LIB" compare-behind-by "$1" "$2" 2>/dev/null || echo 0
  else
    gh api "repos/$REPO/compare/$1...$2" --jq '.behind_by // 0' 2>/dev/null || echo 0
  fi
}

read_state

is_behind=false
[ "$merge_state" = "BEHIND" ] && is_behind=true
if $FORCE && ! $is_behind; then
  behind=$(behind_count "$base_ref" "$head_ref")
  [ "$behind" -gt 0 ] 2>/dev/null && is_behind=true
fi
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
      # Nothing this run would otherwise do (not behind, not conflicting) —
      # CI running here is mid-flight from a push this run had nothing to do
      # with. An update-branch would just cancel it for no benefit, so skip
      # entirely unless --force said "no matter what": then fall through to
      # the poll loop below instead of calling update-branch on a PR with
      # nothing to merge.
      if ! $FORCE; then
        report "ci-running" "CI checks are already running from a prior push; skipping so this run doesn't cancel them — retry on the next sweep"
        exit 0
      fi
      ;;
  esac
else
  if [ -n "${USE_GH_API:-}" ]; then
    update_err=$(GH_REPO="$REPO" "$LIB" pr-update-branch "$PR" 2>&1) && update_ok=1 || update_ok=0
  else
    update_err=$(gh pr update-branch "$PR" --repo "$REPO" 2>&1) && update_ok=1 || update_ok=0
  fi
  if [ "$update_ok" -eq 0 ]; then
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
      # An empty rollup right after this run pushed a merge commit means
      # GitHub has not created that head's workflow runs yet — not that the
      # PR has no CI. Accepting it as green (as this loop used to) ended the
      # run seconds before the CI it triggered existed: the caller then
      # reviewed and merged on checks that never ran, and the next sweep
      # found that same run mid-flight and skipped the PR as `ci-running`.
      # Give the checks a bounded window to appear; if none ever do, the
      # repo genuinely has no PR CI and `fixed` was right after all.
      if [ "$ci_state" = "none" ] && $did_update \
         && [ "$attempt" -lt "$NO_CHECKS_GRACE_ATTEMPTS" ]; then
        awaiting_new_checks=true
      else
        awaiting_new_checks=false
      fi
      if ! $awaiting_new_checks; then
        if $did_update; then
          report "fixed" "branch updated, checks are $ci_state"
        else
          report "fixed" "branch was already current; checks finished as $ci_state"
        fi
        exit 0
      fi
      ;;
    failure)
      report_and_flag_needs_work "still-failing" "branch is current with base, but checks are failing" ;;
  esac
  attempt=$((attempt + 1))
  [ "$attempt" -lt "$POLL_MAX_ATTEMPTS" ] && sleep "$POLL_INTERVAL_SECONDS"
done

report "pending-timeout" "checks still running after $POLL_MAX_ATTEMPTS polls"
