---
name: plan-next-issue
description: Automated planning-stage worker for the AgentHarness pipeline. Claims one ready `agent` issue (or resumes a stale `agent-planning` claim), runs analyst through planner, opens a draft PR, and hands off to /implement-next-task. Triggered on a schedule; not normally invoked directly by a human.
---

You run one bounded cycle of the planning stage of the AgentHarness
pipeline: claim (or resume) one issue, run it through analyst -> architect
-> designer -> planner, open a draft PR, and exit. You never touch the
implementing/developer/review/code-review phases -- that is
`/implement-next-task`'s job, triggered separately.

This skill **always works inside a dedicated git worktree**, the same
convention `/oneshot` uses -- never run against the primary checkout.

## Naming convention

Identical to `/oneshot`'s: branch and worktree directory both use the
strict, deterministic form `feature/{issue_id}-{Title-Slug}`. See
`.claude/skills/oneshot/SKILL.md`'s "Naming convention" section for the
exact slug derivation pipeline -- `claim_issue.sh` and `find_candidate.sh`
in this skill already implement it identically; do not re-derive it by
hand.

## What you do

1. **Check concurrency.** Refuse to start a new planning cycle if too many
   are already running on this machine:

```bash
.claude/skills/plan-next-issue/check_concurrency.sh "${PLAN_MAX_CONCURRENT:-2}" \
  "claude.*--dangerously-skip-permissions.*plan-next-issue"
```

   Exit code `4` means at capacity -- report "planning at capacity, skipping
   this cycle" and stop here. Do not claim anything.

2. **Find a candidate.** Check the script's own exit status explicitly --
   do not rely on `.candidate` alone. `find_candidate.sh` can fail (e.g. a
   transient `gh api` error) and still print something to stdout; without
   an exit-status check, a non-zero exit could leave `$RESULT` empty or
   partial, `jq -r '.candidate'` on that would not reliably produce the
   literal string `"null"`, and this step would proceed into `gh issue
   edit ""` and a worktree named `feature/-`:

```bash
set +e
RESULT=$(.claude/skills/plan-next-issue/find_candidate.sh)
FIND_EXIT=$?
set -e
if [ "$FIND_EXIT" -ne 0 ] || [ -z "$RESULT" ]; then
  # report "find_candidate.sh failed (exit $FIND_EXIT)" and stop -- same
  # "nothing to do this cycle" treatment as a null candidate, but logged
  # as an error, not an empty queue
  exit 0
fi
CANDIDATE=$(echo "$RESULT" | jq -r '.candidate')
if [ "$CANDIDATE" = "null" ]; then
  # report "nothing to plan" (include .skipped if non-empty) and stop
  SKIPPED=$(echo "$RESULT" | jq -r '.skipped // []')
  exit 0
fi
ISSUE_ID=$(echo "$RESULT" | jq -r '.candidate.number')
SOURCE=$(echo "$RESULT" | jq -r '.candidate.source')
```

3. **Claim it, if fresh.** If `SOURCE == "fresh"`:

```bash
BRANCH=$(.claude/skills/plan-next-issue/claim_issue.sh "$ISSUE_ID" agent-planning)
SLUG=${BRANCH#feature/${ISSUE_ID}-}
```

   - Exit `0`: `$BRANCH` holds the claimed branch name, `$SLUG` is extracted from it, proceed to step 4.
   - Exit `3`: another runner claimed it first (race). Report and stop --
     do not retry within this invocation; the next scheduled cycle will
     pick a different candidate.
   - Any other exit: a real failure; report it and stop.

   If `SOURCE == "stale-reclaim"`, skip claiming and look up the existing branch directly:

```bash
BRANCH=$(git ls-remote --heads origin "feature/${ISSUE_ID}-*" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
SLUG=${BRANCH#feature/${ISSUE_ID}-}
```

   This mirrors `find_candidate.sh`'s own branch lookup. Both `$BRANCH` and `$SLUG` are now set; proceed to step 4.

4. **Create and enter a dedicated worktree** on `$BRANCH`:

```bash
REPO_ROOT=$(git rev-parse --show-toplevel)   # captured now, from the primary checkout, for the cleanup step below
WORKTREE="../worktrees/feature-${ISSUE_ID}-${SLUG}"
git fetch origin "$BRANCH" 2>/dev/null || true
if git ls-remote --heads origin "$BRANCH" | grep -q .; then
  git worktree add --track -b "$BRANCH" "$WORKTREE" "origin/$BRANCH" 2>/dev/null \
    || git worktree add "$WORKTREE" "$BRANCH"
else
  git worktree add -b "$BRANCH" "$WORKTREE"
fi
cd "$WORKTREE"
```

5. **Run the planning orchestrator.** There is no `agentharness implement`
   command -- follow `.claude/agents/plan-orchestrator.md`
   (`agentharness/data/claude-agents/plan-orchestrator.md`, installed by
   `agentharness init`) end to end via the Task tool. It runs
   `agentharness checkpoint init {issue_number}` and drives analyst ->
   architect -> designer -> planner, committing each artifact as it goes,
   and prints `Planning complete for feat-{issue_number}. Ready for
   implementing.` when done.

6. **Open a draft PR.** Base = the repository default branch, head =
   `$BRANCH`, **draft**. Resolve the real default branch instead of
   hardcoding `master` -- in a repo whose default branch isn't `master`
   (e.g. `main`), a hardcoded `master` fails this call outright, after the
   issue has already been claimed and planning has already run, wasting
   all of that work. The body states what the issue/feature is (from the
   brief) -- there is no code-review section yet, since implementing
   hasn't run:

```bash
DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name')
PR_URL=$(gh pr create \
  --draft \
  --base "$DEFAULT_BRANCH" \
  --head "$BRANCH" \
  --label agent \
  --title "#${ISSUE_ID}: implementation" \
  --body "$(cat <<EOF
Closes #${ISSUE_ID}

## What the issue was
<description of the feature/problem from the brief>

## Status
Planning complete. Implementing has not started yet -- this PR will fill
in as \`/implement-next-task\` runs.

## Artifacts
- Brief, spec, arch-review, design, and task-plan markdown are committed in this branch.
EOF
)")
.claude/skills/oneshot/ensure_pr_linked.sh "$PR_URL" "$ISSUE_ID"
```

   Reuse `oneshot`'s `ensure_pr_linked.sh` unchanged -- the `agent`
   label / `Closes #N` / title-format guarantees it enforces apply here
   too.

7. **Hand off.** Create the target label if it doesn't exist yet (best-effort,
   idempotent -- `gh issue edit --add-label` errors on an unknown label, and
   this swap is on the critical path for the whole handoff), then swap the
   label:

```bash
gh label create agent-ready-for-dev --color 5319e7 \
  --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
gh issue edit "$ISSUE_ID" --remove-label agent-planning --add-label agent-ready-for-dev
```

8. **Remove the worktree before exiting**, regardless of outcome --
   nothing depends on it surviving, since progress lives in the pushed
   branch and `state.json`:

```bash
cd "$REPO_ROOT"   # back to the primary checkout before removing -- captured in step 4
git worktree remove "$WORKTREE" --force 2>/dev/null || true
```

   This also prevents a leaked worktree directory from blocking a future
   stale-reclaim attempt on the same issue (both forms of `git worktree
   add` fail when the target path already exists).

9. Report: issue number, PR URL, "ready for implementing" -- and stop.
   Do not proceed to any developer/review work; that only happens inside
   `/implement-next-task`.

## If something looks wrong

If `find_candidate.sh` keeps returning the same `stale-reclaim` candidate
across multiple invocations without ever completing, the planning
orchestrator itself may be failing on this specific issue (not just
running slow) -- check `artifacts/feat-{issue_number}/state.json` in the
issue's branch for which phase is stuck, same as debugging `/oneshot`
today.
