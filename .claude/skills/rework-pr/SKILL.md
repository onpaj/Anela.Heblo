---
name: rework-pr
description: Revise one open needs-work PR — claim it, bring it current with the default branch, fix what its review or CI-failure comments describe, and push. Use when the user says "rework-pr", "revise PR N", "fix up this needs-work PR", or gives a specific PR number to rework.
---

You revise one PR that's labelled `needs-work` — whether that came from
`/automerge-pr`'s code review or its hygiene auto-reject — read what it
says is wrong, fix the code, and push. Called directly for one PR, or by
`/rework-all` as part of a full-backlog sweep.

**All deterministic work is done by the scripts beside this file.** Do not
re-implement their logic or hand-write the `gh` commands they already own.
Your judgement calls are: reading the review/CI feedback and fixing the
code, and resolving any real conflict when syncing with the default branch.

One PR per invocation. Run this skill again for the next one.

## 0. Resolve `$REPO` first

Every script beside this file detects the repo explicitly and passes
`--repo "$REPO"` to each `gh` call. The `gh` commands in *this* file must do
the same — step 3 onward runs inside a worktree at a different path, where
`gh`'s implicit cwd-based repo resolution is unreliable. Resolve it once,
up front, the same way `find_candidate.sh` does:

```bash
REPO="${GH_REPO:-$(git remote get-url origin | sed -e 's#.*github\.com[:/]##' -e 's#\.git$##')}"
```

If that does not produce an `owner/name` pair, stop and say so — do not
fall back to implicit resolution. Use `--repo "$REPO"` on every `gh`
invocation below.

## 1. Resolve the target PR

If a PR number was given in your invocation, use it as `{N}` and skip to
step 2 (still confirm it's `OPEN` there — an explicit number bypasses
candidate *search*, not the open-state check). Otherwise:

```bash
.claude/skills/rework-pr/find_candidate.sh > /tmp/rework-candidate.json
```

Writes `{"candidate": {...}|null, "skipped": [...]}` to
`/tmp/rework-candidate.json`. Do not second-guess the cap, the live-label
check, or the `agent-wip` skip — read fields with `jq`, never interpolate
PR-derived text (like `headRefName`) directly into a shell string.

If `candidate` is `null`
(`jq -e '.candidate == null' /tmp/rework-candidate.json`), print
`No needs-work PRs ready to revise.`, list `skipped` with reasons, and
stop. Otherwise set `{N}` from `.candidate.number`.

## 2. Confirm it's open, then claim it

```bash
gh pr view {N} --repo "$REPO" --json state --jq .state
```

If the result is not `OPEN`, report this PR as skipped (not pushed to) and
**stop** — do not proceed to step 3 or beyond.

Otherwise, claim it immediately, before any branch work starts. The label
may not exist in the repo yet — create it best-effort first, the same
pattern `apply_verdict.sh` already uses for `needs-work`/`agent-merged`:

```bash
gh label create agent-wip --repo "$REPO" --color fbca04 \
  --description "Claimed by an in-progress /rework-pr run" >/dev/null 2>&1 || true
gh pr edit {N} --repo "$REPO" --add-label agent-wip
```

**From this point on, release this claim on ANY exit, for any reason** —
including a script exiting non-zero, a `git` command failing, an unexpected
error, or running out of turns mid-task:

```bash
gh pr edit {N} --repo "$REPO" --remove-label agent-wip
```

The four paths called out below are the common cases, **not an exhaustive
list** — the rule is general. Nothing sweeps a leaked `agent-wip` label:
`find_candidate.sh` and `list_candidates.sh` skip a PR carrying it forever,
with no TTL, so a claim you fail to release takes that PR out of the
backlog permanently.

Common cases, as illustrations:

1. Not open — already handled above.
2. Unresolved conflict in step 4.
3. Exhausted push retries in step 6.
4. Success — `finish_revision.sh` releases the claim for you (it does so
   *first*, before its comment and `needs-work` steps, for exactly this
   reason).

Release the claim even on a path that also leaves `needs-work` in place —
the claim and the `needs-work` label are independent; releasing the claim
just makes this PR visible to the next
`find_candidate.sh`/`list_candidates.sh` run again.

## 3. Check out the PR's branch

The PR's branch already exists — it was created by `oneshot`. Reuse its
worktree convention:

```bash
HEAD_REF=$(jq -r '.candidate.headRefName // empty' /tmp/rework-candidate.json)
# If you took the explicit-PR-number path in step 1, HEAD_REF came from
# `gh pr view {N} --repo "$REPO" --json headRefName --jq .headRefName` instead.
WORKTREE="../worktrees/$(echo "$HEAD_REF" | sed 's#/#-#')"

if [ -d "$WORKTREE" ]; then
  git -C "$WORKTREE" fetch origin "$HEAD_REF"
  git -C "$WORKTREE" reset --hard "origin/$HEAD_REF"
else
  git worktree add "$WORKTREE" "$HEAD_REF"
fi
```

All edits, commits, and the push happen inside `$WORKTREE` — never against
the primary checkout.

## 4. Sync with the default branch — merge, not rebase

```bash
DEFAULT_BRANCH=$(gh repo view "$REPO" --json defaultBranchRef --jq .defaultBranchRef.name)
git -C "$WORKTREE" fetch origin "$DEFAULT_BRANCH"
git -C "$WORKTREE" merge "origin/$DEFAULT_BRANCH" --no-edit
```

Use `merge`, never `rebase`, here: these branches are periodically synced
via merge commits already in their history, and replaying their original
linear commits with `git rebase` manufactures false conflicts on history a
plain `git merge` reconciles cleanly. `defaultBranchRefName` is **not** a
valid `gh repo view` field — always use `defaultBranchRef.name`.

If the merge reports conflicts, resolve them as a judgement call — same
tier as reading review feedback in step 5. If a conflict's intent is
genuinely unclear (e.g. the same line changed two incompatible ways for
reasons you can't determine from context), abort the merge
(`git -C "$WORKTREE" merge --abort`), release the `agent-wip` claim (step
2's release command), report this PR as skipped with the reason, and
**stop** — do not proceed to step 5.

Because merge only adds commits and never rewrites history, the push in
step 6 stays a plain `git push` — no `--force-with-lease` needed.

## 5. Read the feedback and revise the code

Gather the PR's full review history before touching any code — not just
the latest `/automerge-pr` block, so context from earlier rounds or a
human's inline notes is not lost:

```bash
gh pr view {N} --repo "$REPO" --json title,body,comments,reviews
gh api "repos/$REPO/pulls/{N}/comments"
gh pr diff {N} --repo "$REPO"
```

This includes any hygiene auto-reject comment `/automerge-pr` posted
(`Hygiene check failed for this PR before code review...`) — treat a CI
failure it describes the same way you'd treat a code-review finding: read
it, identify the concrete problem, fix it directly in `$WORKTREE`. If the
feedback is too vague to act on directly, make a good-faith improvement
rather than aborting.

## 6. Commit and push, with retry

Stage only the files you actually changed — never `git add -A`.

```bash
git -C "$WORKTREE" add <files>
git -C "$WORKTREE" commit -m "fix: address /automerge-pr review feedback"
```

Attempt the push, retrying on a non-fast-forward rejection (something else
wrote to the branch mid-run — not necessarily this skill):

```bash
attempt=1
while [ "$attempt" -le 3 ]; do
  if git -C "$WORKTREE" push origin "HEAD:$HEAD_REF"; then
    break
  fi
  if [ "$attempt" -eq 3 ]; then
    # Exhausted retries — stop. Do not call finish_revision.sh: needs-work
    # must stay on a PR whose fix did not actually land.
    gh pr edit {N} --repo "$REPO" --remove-label agent-wip
    echo "push failed after 3 attempts; report what landed on the branch that this run did not push"
    exit 1
  fi
  git -C "$WORKTREE" fetch origin "$HEAD_REF"
  # Same judgement rule as step 4, with the same explicit conflict branch:
  # merge in what's there; if the merge itself fails, resolve any real
  # conflict and re-run the merge, or abort+release+stop if intent is
  # unclear. Never let a failed merge fall through into another push.
  if ! git -C "$WORKTREE" merge "origin/$HEAD_REF" --no-edit; then
    # Resolve the conflicting files and `git -C "$WORKTREE" commit --no-edit`
    # to complete the merge if the intent is clear. Otherwise:
    git -C "$WORKTREE" merge --abort
    gh pr edit {N} --repo "$REPO" --remove-label agent-wip
    echo "push-retry merge conflicted and its intent was unclear; report this PR as skipped"
    exit 1
  fi
  attempt=$((attempt + 1))
done
```

If the push never succeeds within 3 attempts, this is not a one-off race
anymore — stop (as the block above does), and your report must state what
landed on the branch that this run did not push itself.

## 7. Finish

Write a short summary of what you changed to a file using the **Write
tool** — never interpolate it into a shell command — then:

```bash
.claude/skills/rework-pr/finish_revision.sh --pr {N} --summary-file /tmp/rework-{N}-summary.md
```

This posts the summary as a PR comment, removes `needs-work`, **and
releases the `agent-wip` claim**. On success, remove the worktree:

```bash
git worktree remove "$WORKTREE"
```

## 8. Report

This step is the report contract for **every** exit path, not just the
successful one — `/rework-all` tells its subagents to report exactly what
this step asks for, so an early exit must still answer it.

If you revised the PR: state which PR, what you changed, whether you had to
resolve a merge conflict in step 4 or retry the push in step 6, and the
`skipped` list from step 1 (if you took that path) with reasons — a PR
sitting at the revision cap needs a human to look at it.

If you exited early, state the PR number and exactly one of these outcomes,
with its reason:

| Outcome | Exited at |
|---------|-----------|
| `not-open` — the PR was merged or closed before work started | step 2 |
| `conflict resolution declined` — a step 4 (or step 6 push-retry) merge conflict whose intent was unclear; the merge was aborted and nothing was pushed | step 4 / step 6 |
| `push retries exhausted` — 3 rejected pushes; say what landed on the branch that this run did not push | step 6 |

In every early-exit case, also confirm the `agent-wip` claim was released
(step 2's blanket rule) and that `needs-work` was left in place.

## Constants

Do not restate these values elsewhere; each lives in exactly one file.

| Constant | Where it lives |
|----------|----------------|
| `MAX_REVISION_ATTEMPTS` | `find_candidate.sh`, `list_candidates.sh` |
| `NEEDS_WORK_LABEL` | `find_candidate.sh`, `list_candidates.sh`, `finish_revision.sh` (must match `automerge-pr/apply_verdict.sh`'s copy) |
| `AGENT_WIP_LABEL` | `find_candidate.sh`, `list_candidates.sh`, `finish_revision.sh` (must match this file's own `agent-wip` literal in steps 2 and 6) |
| push retry cap (`3`) | this file, step 6 — no script owns it |

## Limits worth knowing

This skill's revision is not independently reviewed before `needs-work`
comes off — the next signal is whatever `/automerge-pr` says next time it
runs. A confidently-wrong revision looks identical to a correct one until
then. There is no confirmation prompt. Watch the first few runs.

The revision-attempt cap counts prior `/automerge-pr` rejections (`verdict:
REJECT` comments, whether from a code review or a hygiene auto-reject), not
`/rework-pr` runs — a PR a human re-labelled `needs-work` by hand always
looks like zero prior attempts to this skill.

The `agent-wip` claim only protects against other `rework-pr` runs. It does
not stop `hygiene-pr` from running `gh pr update-branch` on this PR
concurrently, or `automerge-pr` from reactively calling `hygiene-pr` on it
mid-revision. Running `/rework-all` at the same time as `/hygiene-all` or
`/automerge-all` on overlapping PRs is not covered by this design; treat
that combination as running one family at a time until a real conflict is
observed.

Push retries are capped at 3, not unbounded — a PR under sustained
concurrent writes from something other than this skill will still end up
`needs-work` for a human after the third rejected push.
