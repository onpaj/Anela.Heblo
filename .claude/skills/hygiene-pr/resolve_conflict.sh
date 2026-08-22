#!/usr/bin/env bash
# Resolve one PR's merge conflict with its base branch. Everything except
# the actual edit to a conflicted file is deterministic and lives here; the
# caller drives three steps around its own judgement:
#
#   resolve_conflict.sh --pr N --step prepare
#     -> claims the PR (`agent-wip`), builds/refreshes a worktree at the PR
#        head, and attempts `git merge origin/BASE`. Reports the conflicted
#        files for the caller to edit.
#   resolve_conflict.sh --pr N --step finish
#     -> stages what the caller resolved, refuses to push leftover conflict
#        markers, commits the merge, pushes (with retry), then releases the
#        claim and removes the worktree.
#   resolve_conflict.sh --pr N --step abort --detail "why"
#     -> cleans up the merge/worktree, releases the claim, and flags the PR
#        `needs-work` so it stays discoverable by /rework-pr and by a human.
#
# Emits JSON: {"pr": N, "step": "...", "status": "...", "detail": "...",
#              "worktree": "...", "base": "...", "head": "...", "files": [...]}
# Statuses, per step:
#   prepare  conflicted | merged-clean | claimed-elsewhere | not-open | error
#   finish   pushed | unresolved | push-failed | error
#   abort    flagged
# Always exits 0 once arguments validate — this script reports, it never
# fails the caller over a PR-hygiene outcome. (A missing/unknown argument
# still exits 1, matching the sibling scripts' convention.)
#
# The `agent-wip` claim is the same label /rework-pr uses, deliberately:
# both skills push to a PR's branch, and neither may do so while the other
# is mid-revision. A PR already carrying it is reported `claimed-elsewhere`
# and left completely untouched.
set -uo pipefail

# Resolved from this script's own location, not the cwd: `finish`/`abort`
# run git commands inside a worktree elsewhere on disk, and the caller may
# not be sitting at the repo root.
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
LIB="$SCRIPT_DIR/../_lib/gh_api.sh"
FLAG_NEEDS_WORK="$SCRIPT_DIR/../_lib/flag_needs_work.sh"

AGENT_WIP_LABEL="agent-wip"
# Must match rework-pr/SKILL.md's own push-retry cap: something else wrote
# to the branch mid-run, which is a race worth retrying, not a conflict.
PUSH_MAX_ATTEMPTS=3

PR=""; STEP=""; DETAIL_ARG=""
while [ $# -gt 0 ]; do
  case "$1" in
    --pr)     PR="$2"; shift 2 ;;
    --step)   STEP="$2"; shift 2 ;;
    --detail) DETAIL_ARG="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

[ -n "$PR" ] || { echo "--pr is required" >&2; exit 1; }
case "$STEP" in
  prepare|finish|abort) ;;
  *) echo "unknown step: ${STEP:-<missing>} (want prepare|finish|abort)" >&2; exit 1 ;;
esac

WORKTREE=""; BASE_REF=""; HEAD_REF=""; FILES_JSON="[]"

report() {  # status, detail
  jq -n --argjson pr "$PR" --arg step "$STEP" --arg status "$1" --arg detail "$2" \
    --arg worktree "$WORKTREE" --arg base "$BASE_REF" --arg head "$HEAD_REF" \
    --argjson files "$FILES_JSON" \
    '{pr: $pr, step: $step, status: $status, detail: $detail,
      worktree: $worktree, base: $base, head: $head, files: $files}'
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

# --- GitHub -----------------------------------------------------------------

# Emits state, comma-joined label names, base ref and head ref — one per
# line, not a TSV: a PR with no labels has an empty field in the middle, and
# `read` with IFS=tab collapses consecutive tabs (tab is IFS whitespace), so
# a TSV would silently shift head_ref out of existence on exactly those PRs.
pr_read() {
  local raw
  if [ -n "${USE_GH_API:-}" ]; then
    raw=$(GH_REPO="$REPO" "$LIB" pr-view "$PR" 2>&1) || { echo "$raw"; return 1; }
  else
    raw=$(gh pr view "$PR" --repo "$REPO" \
      --json state,labels,baseRefName,headRefName 2>&1) || { echo "$raw"; return 1; }
  fi
  printf '%s' "$raw" | jq -r \
    '[.state, ((.labels // []) | map(.name) | join(",")), .baseRefName, .headRefName] | .[]' \
    2>/dev/null || { echo "could not parse gh pr view output: $raw"; return 1; }
}

# Splits pr_read's four lines into the caller's variables.
read_pr_fields() {  # lines
  { IFS= read -r state
    IFS= read -r labels
    IFS= read -r BASE_REF
    IFS= read -r HEAD_REF
  } <<< "$1"
}

label_add() {  # label
  if [ -n "${USE_GH_API:-}" ]; then
    GH_REPO="$REPO" "$LIB" label-create "$1" fbca04 "Claimed by an in-progress /rework-pr or /hygiene-pr run" >/dev/null 2>&1 || true
    GH_REPO="$REPO" "$LIB" pr-edit "$PR" --add-label "$1" 2>&1
  else
    gh label create "$1" --repo "$REPO" --color fbca04 \
      --description "Claimed by an in-progress /rework-pr or /hygiene-pr run" >/dev/null 2>&1 || true
    gh pr edit "$PR" --repo "$REPO" --add-label "$1" 2>&1
  fi
}

label_remove() {  # label
  if [ -n "${USE_GH_API:-}" ]; then
    GH_REPO="$REPO" "$LIB" pr-edit "$PR" --remove-label "$1" 2>&1
  else
    gh pr edit "$PR" --repo "$REPO" --remove-label "$1" 2>&1
  fi
}

release_claim() { label_remove "$AGENT_WIP_LABEL" >/dev/null 2>&1 || true; }

# --- git --------------------------------------------------------------------

TOPLEVEL=$(git rev-parse --show-toplevel 2>/dev/null) || {
  echo "not inside a git repository" >&2; exit 1
}

# Same path convention as rework-pr/SKILL.md step 3, so the two skills share
# one worktree per branch instead of each building their own.
worktree_for() {  # head_ref
  echo "$(dirname "$TOPLEVEL")/worktrees/$(echo "$1" | sed 's#/#-#')"
}

# Where prepare records what it conflicted on, so finish scans exactly those
# files for leftover markers rather than every file in the repo (a repo can
# legitimately contain `<<<<<<<` in a fixture or in prose about conflicts).
# --absolute-git-dir, not --git-dir: the latter can answer with a relative
# path, which would then resolve against the caller's cwd instead of the
# worktree and silently split the two steps' view of this file.
conflicted_list_file() {
  echo "$(git -C "$WORKTREE" rev-parse --absolute-git-dir 2>/dev/null)/hygiene-conflicted-files"
}

# Builds a JSON array from the paths passed as arguments. Paths are carried
# around in bash arrays rather than a delimited string: a filename can
# contain anything except NUL, and bash strings cannot contain NUL at all.
files_json_from_args() {  # path...
  jq -n '$ARGS.positional' --args ${1+"$@"}
}

remove_worktree() {
  [ -n "$WORKTREE" ] && [ -d "$WORKTREE" ] || return 0
  git -C "$WORKTREE" merge --abort >/dev/null 2>&1 || true
  git -C "$TOPLEVEL" worktree remove --force "$WORKTREE" >/dev/null 2>&1 \
    || rm -rf "$WORKTREE"
  git -C "$TOPLEVEL" worktree prune >/dev/null 2>&1 || true
}

# --- steps ------------------------------------------------------------------

if [ "$STEP" = "abort" ]; then
  # Deliberately best-effort and order-independent: abort is the path taken
  # when something already went wrong, so every part of it tolerates the
  # previous part never having happened. Reading the PR is optional here —
  # the claim release and the needs-work flag only need its number.
  if state_line=$(pr_read); then
    read_pr_fields "$state_line"
    WORKTREE=$(worktree_for "$HEAD_REF")
    remove_worktree
  fi
  release_claim
  detail="${DETAIL_ARG:-merge conflict could not be resolved automatically}"
  if ! flag_out=$(GH_REPO="$REPO" "$FLAG_NEEDS_WORK" \
      --pr "$PR" --status conflict --detail "$detail" 2>&1); then
    detail="$detail (could not flag needs-work: $flag_out)"
  fi
  report "flagged" "$detail"
  exit 0
fi

state_line=$(pr_read) || { report "error" "gh pr view failed: $state_line"; exit 0; }
read_pr_fields "$state_line"
WORKTREE=$(worktree_for "$HEAD_REF")

if [ "$STEP" = "prepare" ]; then
  if [ "$state" != "OPEN" ]; then
    WORKTREE=""
    report "not-open" "PR is $state; nothing to resolve"
    exit 0
  fi
  if [[ ",$labels," == *",$AGENT_WIP_LABEL,"* ]]; then
    WORKTREE=""
    report "claimed-elsewhere" "PR already carries $AGENT_WIP_LABEL; another run owns this branch"
    exit 0
  fi

  # Claim before touching the branch, exactly as /rework-pr does. This
  # check-then-claim narrows the race but is not an atomic lock — GitHub's
  # label API has no compare-and-set.
  if ! claim_out=$(label_add "$AGENT_WIP_LABEL"); then
    report "error" "could not claim PR with $AGENT_WIP_LABEL: $claim_out"
    exit 0
  fi

  fail_prepare() {  # detail
    remove_worktree
    release_claim
    report "error" "$1"
    exit 0
  }

  git -C "$TOPLEVEL" fetch origin "$BASE_REF" "$HEAD_REF" >/dev/null 2>&1 \
    || fail_prepare "could not fetch $BASE_REF/$HEAD_REF from origin"
  git -C "$TOPLEVEL" worktree prune >/dev/null 2>&1 || true

  if [ -d "$WORKTREE" ]; then
    # A worktree left behind by an earlier run (or by /rework-pr) is reused
    # rather than fought with — but only after being reset to exactly the
    # PR head, so no stale merge state or edit leaks into this resolution.
    git -C "$WORKTREE" merge --abort >/dev/null 2>&1 || true
    git -C "$WORKTREE" checkout --detach --force "origin/$HEAD_REF" >/dev/null 2>&1 \
      || fail_prepare "could not check out origin/$HEAD_REF in $WORKTREE"
    git -C "$WORKTREE" reset --hard "origin/$HEAD_REF" >/dev/null 2>&1 \
      || fail_prepare "could not reset $WORKTREE to origin/$HEAD_REF"
    git -C "$WORKTREE" clean -fdq >/dev/null 2>&1 || true
  else
    # Detached on purpose: the local branch may be checked out in the main
    # working copy, and this run has no business moving it. The push in
    # `finish` uses `HEAD:$HEAD_REF`, so no local branch is needed.
    git -C "$TOPLEVEL" worktree add --detach "$WORKTREE" "origin/$HEAD_REF" >/dev/null 2>&1 \
      || fail_prepare "could not create worktree at $WORKTREE"
  fi

  if git -C "$WORKTREE" merge "origin/$BASE_REF" --no-edit >/dev/null 2>&1; then
    : > "$(conflicted_list_file)"
    report "merged-clean" "origin/$BASE_REF merged into the PR head with no conflict; nothing to resolve by hand"
    exit 0
  fi

  conflicted=()
  while IFS= read -r -d '' path; do
    conflicted+=("$path")
  done < <(git -C "$WORKTREE" diff --name-only --diff-filter=U -z 2>/dev/null)
  if [ "${#conflicted[@]}" -eq 0 ]; then
    # The merge failed without leaving conflicted paths — a dirty worktree,
    # a missing ref, something structural. Not a conflict a caller can edit
    # its way out of, so don't leave the claim or the half-merge behind.
    fail_prepare "git merge origin/$BASE_REF failed without producing conflicted files"
  fi
  printf '%s\0' "${conflicted[@]}" > "$(conflicted_list_file)"
  FILES_JSON=$(files_json_from_args "${conflicted[@]}")
  report "conflicted" "resolve the listed files in $WORKTREE, then re-run with --step finish"
  exit 0
fi

# --- finish -----------------------------------------------------------------

[ -d "$WORKTREE" ] || {
  WORKTREE=""
  report "error" "no prepared worktree for $HEAD_REF; run --step prepare first"
  exit 0
}

# Stage whatever the caller resolved: every path this merge conflicted on
# (recorded by prepare) plus anything git still reports as unmerged. Never
# `git add -A`, which would sweep in scratch files the caller left in the
# worktree — and never only the still-unmerged set: after the first `finish`
# stages them, a second pass would re-scan the stale index content and keep
# reporting the caller's fresh edits as unresolved forever.
list_file=$(conflicted_list_file)
to_stage=()
if [ -s "$list_file" ]; then
  while IFS= read -r -d '' path; do
    to_stage+=("$path")
  done < "$list_file"
fi
while IFS= read -r -d '' path; do
  to_stage+=("$path")
done < <(git -C "$WORKTREE" diff --name-only --diff-filter=U -z 2>/dev/null)

for path in ${to_stage[@]+"${to_stage[@]}"}; do
  git -C "$WORKTREE" add -A -- "$path" >/dev/null 2>&1 || {
    report "error" "could not stage resolved file: $path"; exit 0
  }
done

still_unmerged=$(git -C "$WORKTREE" diff --name-only --diff-filter=U 2>/dev/null)
if [ -n "$still_unmerged" ]; then
  report "error" "files are still unmerged after staging: $still_unmerged"
  exit 0
fi

# Guard against a caller that staged the conflict rather than resolving it:
# a committed `<<<<<<<` block is a broken build at best and silently wrong
# code at worst. Only the files this merge actually conflicted on are
# scanned — see conflicted_list_file() — and only for the unambiguous
# markers: a lone `=======` line is a Markdown setext underline as often as
# it is a conflict, and a false positive here costs a needless rejection.
unresolved=()
if [ -s "$list_file" ]; then
  while IFS= read -r -d '' path; do
    if git -C "$WORKTREE" show ":$path" 2>/dev/null \
        | grep -qE '^(<<<<<<<|>>>>>>>|\|\|\|\|\|\|\|)( |$)'; then
      unresolved+=("$path")
    fi
  done < "$list_file"
fi
if [ "${#unresolved[@]}" -gt 0 ]; then
  FILES_JSON=$(files_json_from_args "${unresolved[@]}")
  report "unresolved" "conflict markers are still present; nothing was pushed and $WORKTREE is left in place to fix"
  exit 0
fi

if git -C "$WORKTREE" rev-parse -q --verify MERGE_HEAD >/dev/null 2>&1; then
  if ! commit_out=$(git -C "$WORKTREE" commit --no-edit 2>&1); then
    report "error" "could not commit the resolved merge: $commit_out"
    exit 0
  fi
fi

attempt=1
while : ; do
  if push_out=$(git -C "$WORKTREE" push origin "HEAD:$HEAD_REF" 2>&1); then
    break
  fi
  if [ "$attempt" -ge "$PUSH_MAX_ATTEMPTS" ]; then
    report "push-failed" "push rejected after $PUSH_MAX_ATTEMPTS attempts: $push_out"
    exit 0
  fi
  # Rejected because something else wrote to the branch mid-run. Merge it in
  # and retry; a conflict *there* is a fresh judgement call this step can't
  # make, so it stops rather than pushing something half-reconciled.
  git -C "$WORKTREE" fetch origin "$HEAD_REF" >/dev/null 2>&1
  if ! merge_out=$(git -C "$WORKTREE" merge "origin/$HEAD_REF" --no-edit 2>&1); then
    git -C "$WORKTREE" merge --abort >/dev/null 2>&1 || true
    report "push-failed" "the branch moved mid-run and re-merging it conflicted: $merge_out"
    exit 0
  fi
  attempt=$((attempt + 1))
done

remove_worktree
release_claim
report "pushed" "conflict resolved and pushed to $HEAD_REF"
exit 0
