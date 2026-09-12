#!/usr/bin/env bash
# Reap orphaned pipeline runs: a CLOSED issue that still carries an
# in-flight stage label.
#
# Every stage's candidate selection lists issues with `--state open`. That
# is correct for picking up work, but it means an issue closed from outside
# the pipeline -- a human closing a duplicate, a `Fixes #N` in someone
# else's PR, a competing agent whose own PR merged first, a stale-cleanup
# sweep -- drops out of the world the moment it closes, still carrying its
# stage label. Its branch and (draft) PR are then unreachable by everything:
# `find_candidate.sh` never sees the issue again, and `automerge-*`,
# `hygiene-*` and `rework-*` all skip drafts. Nothing logged it, nothing
# retried it, nothing flagged it. This pass is the only thing that looks.
#
# Emits JSON: {"orphans": [{number, label, branch, pr, action, reason}]}
# where `action` is one of:
#
#   closed          -- artifact-only branch: PR commented on and closed,
#                      stage label stripped
#   flagged         -- carries real work, or could not be shown not to:
#                      `agent-needs-human` on the issue, `needs-work` on the
#                      PR, PR left OPEN for a human
#   label-stripped  -- nothing to close (no branch, or no open PR); the
#                      stale stage label is removed so it stops being an
#                      orphan
#   skipped         -- nothing was determined safely (branch still
#                      receiving commits inside the staleness window, or a
#                      read failed); left entirely alone, retried next cycle
#
# Reads fail into `skipped`, never into `label-stripped`. The dangerous
# direction here is not "failed to reap" -- it is reading a network error as
# "this issue has no branch/PR", stripping the stage label, and destroying
# the only handle anything still has on the PR. That is the bug this script
# exists to undo, so it must not be able to cause it.
#
# The one thing this must never do is close a branch carrying real work.
# That cannot be read off commit messages -- implementation commits use the
# same `chore(feat-N):` prefix planning artifacts do -- so the signal is
# changed paths: a run that never reached implementation touches nothing
# outside `artifacts/feat-{N}/`. Anything else, and anything this cannot
# determine (a truncated file listing, an unreadable PR), goes to a human
# instead. Losing merged-elsewhere planning artifacts is free; losing an
# implementation is not.
#
# Usage: reap_orphans.sh [--dry-run]
#   --dry-run   classify and report, change nothing on GitHub
#
# Env: GH_REPO (else auto-detected from origin), STALE_MINUTES (default 10),
#      NOW_OVERRIDE (tests), USE_GH_API (curl+REST transport).
set -euo pipefail

# When USE_GH_API is set, every `gh` call below routes through the shared
# curl+REST library instead — for environments where the `gh` CLI itself is
# not permitted. See .claude/skills/_lib/gh_api.sh for the transport layer;
# the logic here is unchanged either way.
LIB="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/gh_api.sh"

STAGE_LABELS=("agent-planning" "agent-ready-for-dev" "agent-implementing")
NEEDS_HUMAN_LABEL="agent-needs-human"
NEEDS_WORK_LABEL="needs-work"
FLAG_COLOR="d93f0b"

DRY_RUN=false
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=true; shift ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

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

# ---- transport ------------------------------------------------------------

issue_list_json() {  # state, label
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" issue-list "$1" "$2"
  else
    gh issue list --repo "$REPO" --state "$1" --label "$2" --limit 100 --json number,title,createdAt
  fi
}

commit_data_for() {  # ref
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" GET "repos/$REPO/commits/$1" 2>/dev/null || echo ""
  else
    gh api "repos/$REPO/commits/$1" 2>/dev/null || echo ""
  fi
}

# Both transports exit non-zero for "this branch has no PR" *and* for "the
# API call failed", and only the first is an orphan with nothing left to
# close -- reading the second as the first would strip the stage label and
# lose the only handle on a live PR. The message decides, so it is captured
# in a file rather than a variable (a command substitution's assignments do
# not survive its subshell).
PR_ERR_FILE=$(mktemp)

# Every action this script takes is irreversible and this JSON is its only
# record, so it is emitted from the EXIT trap rather than from the end of the
# script. Anything that ends the run early under `set -euo pipefail` -- a
# truncated API response that jq chokes on, an unhandled edge in a later
# pool -- would otherwise throw away the record of the PRs already commented
# on and closed in earlier ones. The caller treats a non-zero exit as
# non-fatal to the cycle precisely so this record still gets read.
orphans="[]"
ORPHANS_EMITTED=false

emit_orphans() {
  [ "$ORPHANS_EMITTED" = true ] && return 0
  ORPHANS_EMITTED=true
  jq -n --argjson orphans "$orphans" '{orphans: $orphans}' 2>/dev/null \
    || printf '{"orphans": []}\n'
}

trap 'emit_orphans; rm -f "$PR_ERR_FILE"' EXIT

pr_json_for() {  # branch or PR number -- non-zero on failure, msg in $PR_ERR_FILE
  : > "$PR_ERR_FILE"
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-view "$1" files 2>"$PR_ERR_FILE"
  else
    gh pr view "$1" --repo "$REPO" --json number,state,isDraft,changedFiles,files 2>"$PR_ERR_FILE"
  fi
}

pr_lookup_says_no_pr() {  # the failure message means "no PR", not "call failed"
  # Deliberately narrow. Both transports have exactly one phrasing for a
  # branch with no PR -- `gh`: "no pull requests found for branch X",
  # gh_api.sh's _resolve_pr_number: "no PR found for branch 'X'" -- and both
  # carry "for branch". A bare "Not Found"/404 must NOT match: gh_api.sh's
  # emit() renders any 404 that way, so matching it would let an unrelated
  # API failure read as a confirmed "this branch has no PR" and strip the
  # stage label. Erring the other way is safe: an unrecognised phrasing
  # falls through to `skipped`, which is retried and reported, never silent.
  case "$(cat "$PR_ERR_FILE")" in
    *"no pull requests found for branch"*|*"no PR found for branch"*) return 0 ;;
    *) return 1 ;;
  esac
}

ensure_label() {  # name, color, description
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" label-create "$1" "$2" "$3" >/dev/null 2>&1 || true
  else
    gh label create "$1" --repo "$REPO" --color "$2" --description "$3" >/dev/null 2>&1 || true
  fi
}

issue_swap_label() {  # issue, remove, [add]
  # ADD BEFORE REMOVE, always. Under USE_GH_API these are two separate HTTP
  # calls (POST .../labels then DELETE .../labels/{name}), so an
  # interruption between them leaves the issue in whichever half landed.
  # Add-first leaves it over-labelled, which is harmless and still
  # discoverable; remove-first would leave it carrying neither label --
  # invisible to every stage's `--state open` selection AND to this
  # reaper's own closed-issue sweep, i.e. stranded permanently by the very
  # pass that exists to un-strand it.
  local issue="$1" remove="$2" add="${3:-}"
  if [[ -n "${USE_GH_API:-}" ]]; then
    if [ -n "$add" ]; then
      GH_REPO="$REPO" "$LIB" issue-edit "$issue" --add-label "$add" --remove-label "$remove" 2>/dev/null || true
    else
      GH_REPO="$REPO" "$LIB" issue-edit "$issue" --remove-label "$remove" 2>/dev/null || true
    fi
  else
    if [ -n "$add" ]; then
      gh issue edit "$issue" --repo "$REPO" --add-label "$add" --remove-label "$remove" 2>/dev/null || true
    else
      gh issue edit "$issue" --repo "$REPO" --remove-label "$remove" 2>/dev/null || true
    fi
  fi
}

pr_add_label() {  # pr, label
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-edit "$1" --add-label "$2" 2>/dev/null || true
  else
    gh pr edit "$1" --repo "$REPO" --add-label "$2" 2>/dev/null || true
  fi
}

pr_comment_file() {  # pr, file
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-comment "$1" "$2" 2>/dev/null || true
  else
    gh pr comment "$1" --repo "$REPO" --body-file "$2" 2>/dev/null || true
  fi
}

pr_close() {  # pr
  if [[ -n "${USE_GH_API:-}" ]]; then
    GH_REPO="$REPO" "$LIB" pr-close "$1" 2>/dev/null || true
  else
    gh pr close "$1" --repo "$REPO" 2>/dev/null || true
  fi
}

# ---- helpers --------------------------------------------------------------

epoch_of() {  # iso8601 -- empty when unparseable, on GNU or BSD date
  date -u -d "$1" +%s 2>/dev/null || date -u -j -f "%Y-%m-%dT%H:%M:%SZ" "$1" +%s 2>/dev/null || echo ""
}

comment_with() {  # pr, body
  local file
  file=$(mktemp)
  printf '%s' "$2" > "$file"
  pr_comment_file "$1" "$file"
  rm -f "$file"
}

now_epoch=$(epoch_of "${NOW_OVERRIDE:-now}")
[ -n "$now_epoch" ] || now_epoch=$(date -u +%s)

seen=""

record() {  # number, label, branch, pr, action, reason
  orphans=$(echo "$orphans" | jq \
    --argjson n "$1" --arg label "$2" --arg branch "$3" --argjson pr "$4" \
    --arg action "$5" --arg reason "$6" \
    '. + [{number: $n, label: $label, branch: (if $branch == "" then null else $branch end),
           pr: $pr, action: $action, reason: $reason}]')
}

# ---- sweep ----------------------------------------------------------------

for label in "${STAGE_LABELS[@]}"; do
  closed_json=$(issue_list_json closed "$label")
  numbers=$(echo "$closed_json" | jq -r 'sort_by(.createdAt) | .[].number')

  for n in $numbers; do
    # An issue can carry two stage labels if a label swap half-failed --
    # report and act on it once, under whichever label was seen first.
    case " $seen " in *" $n "*) continue ;; esac
    seen="$seen $n"

    # `git ls-remote` exits non-zero on a transport failure and zero-with-
    # no-output when nothing matches. Only the second means "this issue
    # never got a branch"; treating the first that way would strip the
    # stage label off a run whose branch is fine.
    set +e
    heads=$(git ls-remote --heads origin "feature/${n}-*" 2>/dev/null)
    ls_exit=$?
    set -e
    if [ "$ls_exit" -ne 0 ]; then
      record "$n" "$label" "" null "skipped" "could not list remote branches (git ls-remote exit ${ls_exit}) -- retried next cycle"
      continue
    fi
    # Exactly one match, or nothing is decided here. `feature/${n}-*` is a
    # glob, and two branches for one issue is a real state -- slug drift
    # produces it whenever an issue title is edited after its branch was cut
    # (implement-next-task warns about exactly that). Taking the first match
    # and stripping the stage label would leave the other branch's still-open
    # PR with no handle at all: the permanent stranding this script exists to
    # undo, inflicted by the script itself.
    branch_count=$(echo "$heads" | grep -c 'refs/heads/' || true)
    if [ "$branch_count" -gt 1 ]; then
      record "$n" "$label" "" null "skipped" "${branch_count} branches match feature/${n}-* -- cannot tell which one this run used"
      continue
    fi
    branch=$(echo "$heads" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
    if [ -z "$branch" ]; then
      $DRY_RUN || issue_swap_label "$n" "$label"
      record "$n" "$label" "" null "label-stripped" "no feature/${n}-* branch on origin"
      continue
    fi

    # The glob matches across `/`, so `feature/${n}-anything/at/all` comes
    # back from ls-remote too. Branch names are chosen by whoever can push,
    # and a ref shaped like a PR URL is read as a PR reference by both
    # transports -- `feature/${n}-x/pull/99` would aim this sweep's close at
    # the unrelated PR #99. Only the shape the pipeline actually creates is
    # acted on; anything else is a human's problem, not this script's.
    if [[ ! "$branch" =~ ^feature/${n}-[A-Za-z0-9._-]+$ ]]; then
      record "$n" "$label" "$branch" null "skipped" "branch name is not the shape this pipeline creates -- not acting on it unexamined"
      continue
    fi

    # Never act on a branch that is still being pushed to: a worker can be
    # mid-run when the issue closes underneath it, and closing its PR from
    # another process turns a recoverable mess into a confusing one. The
    # next cycle reaps it once it genuinely goes quiet.
    commit_data=$(commit_data_for "$branch")
    # `gh api` prints its error body to stdout even on a non-zero exit, so
    # `.commit.committer.date` on a failed call is the literal string
    # "null", not empty. Treat the two the same -- and treat both as "do
    # not touch this": with no commit date there is no way to tell an
    # abandoned run from one a worker is pushing to right now.
    commit_date=$(echo "$commit_data" | jq -r '.commit.committer.date' 2>/dev/null || echo "")
    if [ -z "$commit_date" ] || [ "$commit_date" = "null" ]; then
      record "$n" "$label" "$branch" null "skipped" "could not read the branch's last commit date -- cannot tell an abandoned run from a live one"
      continue
    fi
    commit_epoch=$(epoch_of "$commit_date")
    if [ -z "$commit_epoch" ]; then
      record "$n" "$label" "$branch" null "skipped" "could not parse the branch's last commit date (${commit_date})"
      continue
    fi
    if [ $(( (now_epoch - commit_epoch) / 60 )) -lt "$STALE_MINUTES" ]; then
      record "$n" "$label" "$branch" null "skipped" "branch had a commit under ${STALE_MINUTES}min ago -- may still be running"
      continue
    fi

    set +e
    pr_json=$(pr_json_for "$branch")
    pr_exit=$?
    set -e
    if [ "$pr_exit" -ne 0 ]; then
      if pr_lookup_says_no_pr; then
        $DRY_RUN || issue_swap_label "$n" "$label"
        record "$n" "$label" "$branch" null "label-stripped" "no PR for this branch"
      else
        record "$n" "$label" "$branch" null "skipped" "could not read this branch's PR -- retried next cycle"
      fi
      continue
    fi

    pr_number=$(echo "$pr_json" | jq -r '.number // empty' 2>/dev/null || echo "")
    pr_state=$(echo "$pr_json" | jq -r '.state // empty' 2>/dev/null || echo "")
    if [ -z "$pr_number" ]; then
      record "$n" "$label" "$branch" null "skipped" "PR lookup returned no number -- retried next cycle"
      continue
    fi
    if [ -z "$pr_state" ]; then
      record "$n" "$label" "$branch" "$pr_number" "skipped" "PR lookup returned no state -- retried next cycle"
      continue
    fi
    if [ "$pr_state" != "OPEN" ]; then
      $DRY_RUN || issue_swap_label "$n" "$label"
      record "$n" "$label" "$branch" "$pr_number" "label-stripped" "PR already ${pr_state}"
      continue
    fi

    # The safety gate. `changedFiles` is the PR's own count; `files` is a
    # single page of at most 100. If the two disagree, the listing is
    # truncated and "everything listed is an artifact" no longer implies
    # "everything changed is an artifact".
    listed=$(echo "$pr_json" | jq '(.files // []) | length' 2>/dev/null || echo "")
    changed=$(echo "$pr_json" | jq '.changedFiles // 0' 2>/dev/null || echo "")
    foreign=$(echo "$pr_json" | jq -r --arg prefix "artifacts/feat-${n}/" \
      '[(.files // [])[] | .path | select(startswith($prefix) | not)] | length' 2>/dev/null || echo "")

    # All three feed `[ x -ne y ]`, where a non-integer is a fatal bash error
    # under `set -euo pipefail`. That would abort the sweep before the
    # orphans JSON is printed -- discarding the audit record of every close
    # and comment already made this run, which for a script whose actions are
    # irreversible is the worst available failure mode.
    if ! [[ "$listed" =~ ^[0-9]+$ && "$changed" =~ ^[0-9]+$ && "$foreign" =~ ^[0-9]+$ ]]; then
      record "$n" "$label" "$branch" "$pr_number" "skipped" "could not read this PR's file counts -- retried next cycle"
      continue
    fi

    reason=""
    if [ "$listed" -eq 0 ]; then
      reason="PR reports no changed files -- cannot confirm it holds no implementation"
    elif [ "$listed" -ne "$changed" ]; then
      reason="file listing truncated (${listed} of ${changed}) -- cannot confirm it holds no implementation"
    elif [ "$foreign" -ne 0 ]; then
      reason="${foreign} file(s) outside artifacts/feat-${n}/ -- this branch holds real work"
    fi

    # Safe to reap. Comment first (so the explanation is what the close
    # notification carries), then close, then CONFIRM against a fresh read
    # -- a write that returned without erroring is not proof it landed, and
    # a close that silently did nothing while this stripped the stage label
    # and reported "closed" would strand the PR permanently, which is the
    # exact failure this script exists to undo. An unconfirmed close falls
    # through to the human-routed branch below rather than retrying
    # forever: the stage label still has to come off, or every later cycle
    # re-finds this orphan and posts the same comment again.
    # Addressed by number, never by branch. The gate above vetted one
    # specific PR; re-resolving from the branch for each write reopens the
    # question it just answered -- a PR opened for that branch in between, or
    # a transport whose head lookup prefers a different one of several, would
    # receive the close instead of the PR that was checked.
    if [ -z "$reason" ] && ! $DRY_RUN; then
      comment_with "$pr_number" "$(printf 'Closing as superseded.\n\nIssue #%s was closed while this AgentHarness run was still in its pipeline phase. Every stage selects candidates with `--state open`, so this PR could never be picked up again despite its `%s` label -- and draft PRs are invisible to `automerge-*`, `hygiene-*` and `rework-*` too.\n\nThis branch changes only pipeline artifacts under `artifacts/feat-%s/` -- no implementation -- so nothing is lost by closing it. The branch itself is left in place.\n\nReaped automatically by `_lib/reap_orphans.sh`.\n' "$n" "$label" "$n")"
      pr_close "$pr_number"
      after_state=$(pr_json_for "$pr_number" | jq -r '.state // empty' 2>/dev/null || echo "")
      if [ "$after_state" = "OPEN" ] || [ -z "$after_state" ]; then
        reason="the close did not take effect (PR still ${after_state:-unreadable}) -- needs closing by hand"
      fi
    fi

    if [ -n "$reason" ]; then
      if ! $DRY_RUN; then
        ensure_label "$NEEDS_HUMAN_LABEL" "$FLAG_COLOR" "AgentHarness pipeline stage label"
        ensure_label "$NEEDS_WORK_LABEL" "$FLAG_COLOR" "Agent review found blocking problems"
        comment_with "$pr_number" "$(printf 'This PR is orphaned and needs a human.\n\nIssue #%s is **closed** but this branch still carries `%s`. Every stage of the pipeline selects candidates with `--state open`, so nothing will ever pick this run up again, and `automerge-*`/`hygiene-*`/`rework-*` all skip draft PRs.\n\nIt was **not** closed automatically: %s\n\nSomeone needs to decide whether to finish this work, retarget it at another issue, or close it.\n' "$n" "$label" "$reason")"
        pr_add_label "$pr_number" "$NEEDS_WORK_LABEL"
        issue_swap_label "$n" "$label" "$NEEDS_HUMAN_LABEL"
      fi
      record "$n" "$label" "$branch" "$pr_number" "flagged" "$reason"
      continue
    fi

    $DRY_RUN || issue_swap_label "$n" "$label"
    record "$n" "$label" "$branch" "$pr_number" "closed" "artifact-only branch, no implementation to lose"
  done
done

emit_orphans
