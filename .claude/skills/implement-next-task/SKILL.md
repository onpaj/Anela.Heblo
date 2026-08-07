---
name: implement-next-task
description: Automated implementing-stage worker for the AgentHarness pipeline. Claims one issue ready for implementing (or resumes a stale claim), runs exactly one bounded unit of developer/review/code-review work, pushes, and exits -- undrafting the PR only once the whole pipeline is done. Triggered on a schedule; not normally invoked directly by a human.
---

You run one bounded cycle of the implementing stage of the AgentHarness
pipeline: pick up one issue whose planning is done, do **exactly one** unit
of work (one dev task + its review, one code-review round, or the
finishing step), push, and exit. An issue with several dev tasks needs
several separate invocations of this skill -- that is intentional, not a
bug: it is what keeps any single invocation from running long enough to
pile up under the hourly trigger.

## What you do

1. **Check concurrency.** This is the resource-heavy stage (real
   `dotnet build`/`test` runs, or the equivalent for whatever stack the
   target repo uses), so this cap should generally stay at or below
   Planning's:

```bash
.claude/skills/plan-next-issue/check_concurrency.sh "${IMPLEMENT_MAX_CONCURRENT:-2}" \
  "claude.*--dangerously-skip-permissions.*implement-next-task"
```

   Exit code `4` means at capacity -- report "implementing at capacity,
   skipping this cycle" and stop here. Do not claim anything. (This calls
   `plan-next-issue`'s script by path rather than duplicating it -- both
   skill directories always ship together via `agentharness init`, so the
   relative path always resolves.)

2. **Find a candidate.** Check the script's own exit status explicitly --
   do not rely on `.candidate` alone. `find_candidate.sh` can fail (e.g. a
   transient `gh api` error) and still print something to stdout; without
   an exit-status check, a non-zero exit could leave `$RESULT` empty or
   partial, `jq -r '.candidate'` on that would not reliably produce the
   literal string `"null"`, and this step would proceed into `gh issue
   edit ""` and a worktree named `feature/-`:

```bash
set +e
RESULT=$(.claude/skills/implement-next-task/find_candidate.sh)
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
  SKIPPED=$(echo "$RESULT" | jq -r '.skipped // []')
  # report "nothing to implement" (include .skipped if non-empty) and stop
  exit 0
fi
ISSUE_ID=$(echo "$RESULT" | jq -r '.candidate.number')
SOURCE=$(echo "$RESULT" | jq -r '.candidate.source')
```

   If `.candidate` is `null`, report "nothing to implement" (include the
   `.skipped` list if non-empty) and stop.

3. **Claim it, if a fresh handoff.** If `.candidate.source ==
   "fresh-handoff"`, swap the label (advisory only -- see *Concurrency &
   conflict handling* below, this is not a hard lock):

```bash
if [ "$SOURCE" = "fresh-handoff" ]; then
  gh label create agent-implementing --color 5319e7 \
    --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
  gh issue edit "$ISSUE_ID" --remove-label agent-ready-for-dev --add-label agent-implementing
fi
```

   If `.candidate.source == "stale-reclaim"`, the issue already carries
   `agent-implementing` -- no label change needed.

4. **Attach a worktree to the existing branch.** The branch and PR already
   exist (created by `/plan-next-issue`) -- never create a new branch
   here, and never re-derive the branch name from the issue's current
   title: issue titles can be edited after the branch was created (routine
   on agent-managed issues), and re-running the slug-derivation pipeline
   against a live, possibly-edited title can produce a slug that no longer
   matches the real branch, permanently orphaning the issue. Look up the
   actual branch on the remote instead, the same way `plan-next-issue`'s
   own stale-reclaim path does:

```bash
REPO_ROOT=$(git rev-parse --show-toplevel)   # captured now, from the primary checkout, for step 8's cleanup
BRANCH=$(git ls-remote --heads origin "feature/${ISSUE_ID}-*" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
if [ -z "$BRANCH" ]; then
  echo "ERROR: no feature/${ISSUE_ID}-* branch found on origin for issue #${ISSUE_ID}" >&2
  exit 1
fi
SLUG=${BRANCH#feature/${ISSUE_ID}-}
WORKTREE="../worktrees/feature-${ISSUE_ID}-${SLUG}"
git fetch origin "$BRANCH"
git worktree add --track -b "$BRANCH" "$WORKTREE" "origin/$BRANCH" 2>/dev/null \
  || git worktree add "$WORKTREE" "$BRANCH"
cd "$WORKTREE"
```

5. **Run the implementing orchestrator for exactly one unit.** Follow
   `.claude/agents/implement-orchestrator.md`
   (`agentharness/data/claude-agents/implement-orchestrator.md`, installed
   by `agentharness init`) via the Task tool. It reads
   `artifacts/feat-{issue_number}/state.json`, determines the single next
   bounded unit (one dev task + review, one code-review round, or
   finishing), does it, commits, and **pushes** before it stops. It always
   stops after one unit -- it never loops.

6. **Check for a terminal task failure first, before considering
   Finishing.** A developer task that exhausted `max_revisions` makes the
   orchestrator print a message starting `Task {task_name} failed for
   feat-{issue_number} after {N} revisions -- exceeded max_revisions` and
   mark that task `failed` in `state.json` (see
   `implement-orchestrator.md`'s Handling Review Result, `N >=
   max_revisions` branch). This is belt-and-suspenders, same style as step
   7's Finishing check: the message text is one signal, but do not rely on
   it alone -- if this is the ISSUE's only (or last) task, marking it
   `failed` also makes `all_tasks_complete()` true, so `agentharness
   checkpoint status` reports `{"type": "complete"}`, **the exact same
   thing a genuine Finishing outcome reports.** Message text alone cannot
   distinguish the two reliably, so check `state.json`'s tasks list
   directly instead:

```bash
FAILED_TASK=$(jq -r '.tasks[]? | select(.status == "failed") | .name' \
  "artifacts/feat-${ISSUE_ID}/state.json" 2>/dev/null | head -1)

if [ -n "$FAILED_TASK" ]; then
  # Terminal failure: a developer task exhausted max_revisions. Surface it
  # to a human instead of silently marking the pipeline "done" -- do NOT
  # swap to agent-completed for this case. Also swap the issue's own label
  # so it leaves the agent-implementing candidate pool for good: this
  # branch never gets another commit (Finishing is unreachable once a task
  # is failed), so its createdAt/updatedAt stay fixed -- left in
  # agent-implementing, it would keep winning the oldest-wins candidate
  # selection against every newer issue and starve the whole stage.
  LATEST_REVIEW=$(ls -1 "artifacts/feat-${ISSUE_ID}/review/${FAILED_TASK}.r"*.md 2>/dev/null | sort -V | tail -n1)
  gh pr ready "$BRANCH" 2>/dev/null || true   # undraft if still draft, so it's visible
  gh label create needs-work --color d93f0b \
    --description "Agent review found blocking problems" >/dev/null 2>&1 || true
  gh pr edit "$BRANCH" --add-label needs-work 2>/dev/null || true
  gh label create agent-needs-human --color d93f0b \
    --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
  gh issue edit "$ISSUE_ID" --remove-label agent-implementing --add-label agent-needs-human 2>/dev/null || true
  gh pr comment "$BRANCH" --body "$(printf 'Task **%s** failed after exhausting max revisions.\n\nSee `%s` for the last review that requested changes.\n\nThis PR needs a human to look at it -- the automated pipeline cannot make further progress on this task.\n' "$FAILED_TASK" "${LATEST_REVIEW:-review file not found}")" 2>/dev/null || true
  echo "Terminal failure: task ${FAILED_TASK} exhausted max_revisions for feat-${ISSUE_ID}. Flagged needs-work, swapped agent-implementing for agent-needs-human on the issue, and removed it from the implementing queue."
fi
```

   If `$FAILED_TASK` is set, this invocation's outcome is the terminal
   failure above -- skip step 7 (Finishing) entirely, regardless of
   whether the orchestrator also happened to report finishing this round,
   and proceed to step 9 (worktree cleanup).

7. **Otherwise, if the unit that just ran was Finishing** (the orchestrator
   printed `Pipeline complete for feat-{issue_number}. All tasks passed
   review.`): verify artifact state and undraft the PR. Use
   belt-and-suspenders validation to avoid a false positive if the message
   string matches but artifact state does not yet confirm completion:

```bash
TASKS_DONE=$(agentharness checkpoint status "feat-${ISSUE_ID}" 2>/dev/null | grep -q '"type": "complete"' && echo yes || echo no)
FIX_PENDING="artifacts/feat-${ISSUE_ID}/task-context/code-review-fixes.md"

if [ "$TASKS_DONE" = "yes" ] && [ ! -f "$FIX_PENDING" ]; then
  # Artifact state confirms finishing -- surface code review and undraft
  REVIEW_FILE=$(ls -1 artifacts/feat-${ISSUE_ID}/code-review.r*.md 2>/dev/null | sort -V | tail -n1)
  if [ -n "$REVIEW_FILE" ]; then
    gh pr comment "$BRANCH" --body "$(printf '## Code review\n\n%s\n' "$(cat "$REVIEW_FILE")")" 2>/dev/null || true
  fi
  gh pr ready "$BRANCH"
  gh label create agent-completed --color 5319e7 \
    --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
  gh issue edit "$ISSUE_ID" --remove-label agent-implementing --add-label agent-completed
else
  # Orchestrator said finishing but artifact state disagrees -- do not undraft.
  # Treat conservatively as more work remains; next invocation will re-evaluate.
  :
fi
```

   (`gh pr comment` accepts a branch name as the target. `gh pr ready` accepts
   either a PR number or branch. The PR was opened against `$BRANCH` by
   `/plan-next-issue`.)

8. **Otherwise** (more work remains -- a dev task passed, a revision was
   requested, or a code-review round finished with more Blocking
   findings): leave the `agent-implementing` label as-is. Do not undraft
   the PR. The next scheduled invocation (of this same skill, on any
   machine) will pick this issue back up via `find_candidate.sh`.

9. **Always remove the worktree before exiting**, regardless of outcome --
   nothing depends on it surviving, since progress lives in the pushed
   branch and `state.json`:

```bash
cd "$REPO_ROOT"   # back to the primary checkout before removing -- captured in step 4
git worktree remove "$WORKTREE" --force 2>/dev/null || true
```

   `$REPO_ROOT` is whatever the primary checkout's path actually is in
   this repo -- never hardcode it, since this file ships unchanged to
   every consumer repo via the packaged mirror, and a hardcoded path from
   one repo would fail the `cd` in every other one, leaving cwd inside the
   worktree being removed (so `git worktree remove` fails from within its
   own target) and leaking the worktree directory, which then breaks the
   next invocation's worktree-attach step.

10. Report: issue number, unit completed, whether the pipeline finished,
    a terminal task failure was flagged, or more work remains -- and stop.

## Concurrency & conflict handling

**The `agent-ready-for-dev` -> `agent-implementing` claim in step 3 is
advisory, not a true lock** -- `gh issue edit` has no compare-and-set, so
two invocations racing on the exact same fresh handoff could both proceed.
This is accepted, not fixed here, for two reasons: the concurrency cap
(step 1) and the candidate-selection recency window together make the
collision window small in practice, and **git itself is the final
backstop** -- if two workers both do the same unit and both try to push,
only one push can land; the loser's `implement-orchestrator.md` run
reports "lost the race for this unit" (see its Handling Review Result
section) and exits without force-pushing. Worst case is wasted compute on
one duplicate attempt, never corrupted history or lost work.

## If something looks wrong

If `find_candidate.sh` keeps returning the same `stale-reclaim` candidate
across many invocations without the task count in `state.json` ever
advancing, the implementing orchestrator is failing outright on this
issue's current unit (not just running slow) -- check the most recent
`artifacts/feat-{issue_number}/impl/*.md` or `review/*.md` file for what
the developer/reviewer subagent actually reported.
