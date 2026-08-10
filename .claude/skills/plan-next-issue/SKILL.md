---
name: plan-next-issue
description: Automated planning-stage worker for the AgentHarness pipeline. Claims one ready `agent` issue (or resumes a stale `agent-planning` claim), runs analyst through planner, opens a draft PR, and hands off to /implement-next-task. Also accepts an explicit issue number to target and self-heal a specific issue. Triggered on a schedule; not normally invoked directly by a human.
---

You run one bounded cycle of the planning stage of the AgentHarness
pipeline: claim (or resume) one issue, run it through analyst -> architect
-> designer -> planner, open a draft PR, and exit. You never touch the
implementing/developer/review/code-review phases -- that is
`/implement-next-task`'s job, triggered separately.

This skill **always works inside a dedicated git worktree**, the same
convention `/oneshot` uses -- never run against the primary checkout.

**If `USE_GH_API` is set in the environment**, every `gh` invocation shown
below is routed through `.claude/skills/_lib/gh_api.sh` instead -- a
curl+REST equivalent for environments where the `gh` CLI itself is not
permitted. Each bash block below already branches on it; run the block
as-is rather than picking one form by hand.

**You run unattended.** Nobody is watching this run to answer a question --
every decision point below must resolve on its own from repo/GitHub state.
Never use an interactive question/confirmation mechanism to defer a
decision to a human. Where a situation genuinely cannot be resolved safely
(e.g. a merge conflict while catching a stale branch up with the default
branch), the correct move is to stop and report the specific blocker
plainly, the same way step 3's "another runner claimed it first" case
already does -- not to pause and ask.

## Naming convention

Identical to `/oneshot`'s: branch and worktree directory both use the
strict, deterministic form `feature/{issue_id}-{Title-Slug}`. See
`.claude/skills/oneshot/SKILL.md`'s "Naming convention" section for the
exact slug derivation pipeline -- `claim_issue.sh` and `find_candidate.sh`
in this skill already implement it identically; do not re-derive it by
hand.

## Targeting a specific issue (optional)

If invoked with an explicit issue number (e.g. `/plan-next-issue 3853`),
skip `find_candidate.sh` (step 2) entirely and resolve `$ISSUE_ID`,
`$SOURCE`, `$BRANCH`, `$SLUG` yourself so steps 3 onward run unchanged:

```bash
ISSUE_ID="<the given number>"
EXISTING_BRANCH=$(git ls-remote --heads origin "feature/${ISSUE_ID}-*" | head -1 | awk '{print $2}' | sed 's#refs/heads/##')
```

- **No `feature/{ISSUE_ID}-*` branch on origin:** treat exactly like a
  `fresh` candidate -- `SOURCE=fresh`, proceed to step 3's claim call. The
  issue's current labels (e.g. it may carry `agent-wip`, `arch-review`,
  or nothing at all -- it does not need to already carry `agent`) do not
  block this; `claim_issue.sh` only cares whether the branch already
  exists.
- **Branch exists AND a PR already exists for it:** nothing to do --
  another run (or this same run, previously) already got this issue past
  planning. Report the existing PR URL and stop; do not touch labels, the
  branch, or any artifact.
- **Branch exists, no PR exists:** this is a self-heal case -- an earlier
  run (this skill or `/implement-next-task`) created the claim ref and/or
  did real work but died before ever pushing/opening the PR that step 6
  normally guarantees. Recover it autonomously:
  1. `SOURCE=stale-reclaim`; `$BRANCH`/`$SLUG` are already known from
     `$EXISTING_BRANCH` above. Proceed to step 4 to attach a worktree.
  2. Best-effort add the `agent-planning` label so the issue's state is
     visible while this run works on it (create the label first if
     missing, exactly like `claim_issue.sh` does) -- do not remove any
     other label the issue happens to carry; a label like `agent-wip` set
     by an unrelated process is not this skill's to touch:
     ```bash
     if [ -n "${USE_GH_API:-}" ]; then
       .claude/skills/_lib/gh_api.sh label-create agent-planning 5319e7 "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
       .claude/skills/_lib/gh_api.sh issue-edit "$ISSUE_ID" --add-label agent-planning >/dev/null 2>&1 || true
     else
       gh label create agent-planning --color 5319e7 \
         --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
       gh issue edit "$ISSUE_ID" --add-label agent-planning >/dev/null 2>&1 || true
     fi
     ```
  3. **Catch the worktree up with the default branch before doing
     anything else**, in case the claim ref (or the worktree's own base)
     has fallen behind -- a stale claim's base commit can predate commits
     merged to the default branch since the branch was created, and
     pushing without catching up first fails non-fast-forward:
     ```bash
     if [ -n "${USE_GH_API:-}" ]; then
       DEFAULT_BRANCH=$(.claude/skills/_lib/gh_api.sh default-branch)
     else
       DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name')
     fi
     git fetch origin "$DEFAULT_BRANCH"
     git merge "origin/$DEFAULT_BRANCH" -m "chore: merge $DEFAULT_BRANCH to catch up stale claim ref" 2>&1
     ```
     If the merge reports conflicts: **do not attempt to resolve them.**
     Run `git merge --abort`, report exactly which files conflicted and
     that this issue needs a human to reconcile the branch manually, and
     stop -- this is the one situation in this skill that cannot self-heal.
  4. `git push origin HEAD:$BRANCH` to establish the real remote history
     (the previous run's work may have never left this machine).
  5. Read `artifacts/feat-{ISSUE_ID}/state.json` if present. If the
     `planning` phase is already `completed`, **skip step 5** (do not
     re-run analyst/architect/designer/planner against already-finished
     artifacts) and go straight to step 6. Otherwise run step 5 as normal
     -- the planning orchestrator resumes from `state.json` on its own.

## What you do

1. **Check concurrency.** Refuse to start a new planning cycle if too many
   are already running on this machine:

```bash
.claude/skills/plan-next-issue/check_concurrency.sh "${PLAN_MAX_CONCURRENT:-2}" \
  "claude.*--dangerously-skip-permissions.*plan-next-issue"
```

   Exit code `4` means at capacity -- report "planning at capacity, skipping
   this cycle" and stop here. Do not claim anything.

2. **Find a candidate.** (Skip this step entirely if invoked with an
   explicit issue number -- see "Targeting a specific issue" above, which
   sets `$ISSUE_ID`/`$SOURCE` itself.) Check the script's own exit status explicitly --
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

   The title must describe what is actually being implemented, not a
   generic placeholder -- pull it from the spec `planner`/`analyst`
   already produced rather than inventing a new summary:

```bash
if [ -n "${USE_GH_API:-}" ]; then
  DEFAULT_BRANCH=$(.claude/skills/_lib/gh_api.sh default-branch)
else
  DEFAULT_BRANCH=$(gh repo view --json defaultBranchRef --jq '.defaultBranchRef.name')
fi

# spec.r1.md's first line is `# Specification: {descriptive title}` -- use that as the
# PR title's summary. Fall back to the issue's own title if spec.r1.md is missing or its
# first line doesn't match the expected format (never fall back to a placeholder like
# "implementation" -- an unhelpful title is worse than a slightly-off one).
SPEC_TITLE=$(sed -n '1s/^# Specification: //p' "artifacts/feat-${ISSUE_ID}/spec.r1.md" 2>/dev/null)
if [ -z "$SPEC_TITLE" ]; then
  if [ -n "${USE_GH_API:-}" ]; then
    SPEC_TITLE=$(.claude/skills/_lib/gh_api.sh issue-view "$ISSUE_ID" | jq -r '.title')
  else
    SPEC_TITLE=$(gh issue view "$ISSUE_ID" --json title --jq '.title')
  fi
fi

BODY_FILE=$(mktemp)
cat > "$BODY_FILE" <<EOF
Closes #${ISSUE_ID}

## What the issue was
<description of the feature/problem from the brief>

## Status
Planning complete. Implementing has not started yet -- this PR will fill
in as \`/implement-next-task\` runs.

## Artifacts
- Brief, spec, arch-review, design, and task-plan markdown are committed in this branch.
EOF

if [ -n "${USE_GH_API:-}" ]; then
  PR_URL=$(.claude/skills/_lib/gh_api.sh pr-create "$DEFAULT_BRANCH" "$BRANCH" "#${ISSUE_ID}: ${SPEC_TITLE}" "$BODY_FILE" agent)
  # pr-create only sets `draft:true` unconditionally; nothing further needed here.
else
  PR_URL=$(gh pr create \
    --draft \
    --base "$DEFAULT_BRANCH" \
    --head "$BRANCH" \
    --label agent \
    --title "#${ISSUE_ID}: ${SPEC_TITLE}" \
    --body-file "$BODY_FILE")
fi
rm -f "$BODY_FILE"
.claude/skills/oneshot/ensure_pr_linked.sh "$PR_URL" "$ISSUE_ID"
```

   Reuse `oneshot`'s `ensure_pr_linked.sh` unchanged -- the `agent`
   label / `Closes #N` / title-format guarantees it enforces apply here
   too. `ensure_pr_linked.sh` only normalizes the `#<issue>: ` prefix, so
   `$SPEC_TITLE` still drives the descriptive part it leaves alone.

7. **Hand off.** Create the target label if it doesn't exist yet (best-effort,
   idempotent -- `gh issue edit --add-label` errors on an unknown label, and
   this swap is on the critical path for the whole handoff), then swap the
   label:

```bash
if [ -n "${USE_GH_API:-}" ]; then
  .claude/skills/_lib/gh_api.sh label-create agent-ready-for-dev 5319e7 "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
  .claude/skills/_lib/gh_api.sh issue-edit "$ISSUE_ID" --remove-label agent-planning --add-label agent-ready-for-dev
else
  gh label create agent-ready-for-dev --color 5319e7 \
    --description "AgentHarness pipeline stage label" >/dev/null 2>&1 || true
  gh issue edit "$ISSUE_ID" --remove-label agent-planning --add-label agent-ready-for-dev
fi
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

9. **Verify before reporting success.** Do not report "done" just because
   steps 1-8 ran without an error -- confirm the guarantees this skill
   exists to provide actually hold, from the primary checkout (after step
   8's `cd "$REPO_ROOT"`):

```bash
if [ -n "${USE_GH_API:-}" ]; then
  PR_STATE=$(.claude/skills/_lib/gh_api.sh pr-view "$BRANCH" 2>/dev/null)
  ISSUE_LABELS=$(.claude/skills/_lib/gh_api.sh issue-view "$ISSUE_ID" 2>/dev/null | jq -c '[.labels[].name]')
else
  PR_STATE=$(gh pr view "$BRANCH" --json isDraft,state,labels,body 2>/dev/null)
  ISSUE_LABELS=$(gh issue view "$ISSUE_ID" --json labels --jq '[.labels[].name]' 2>/dev/null)
fi

PR_OK=true
echo "$PR_STATE" | jq -e '.isDraft == true' >/dev/null || PR_OK=false
echo "$PR_STATE" | jq -e '.state == "OPEN"' >/dev/null || PR_OK=false
echo "$PR_STATE" | jq -e '[.labels[].name] | index("agent")' >/dev/null || PR_OK=false
echo "$PR_STATE" | jq -e '.body | test("[Cc]los(e|es|ed)\\s+#'"$ISSUE_ID"'")' >/dev/null || PR_OK=false
echo "$ISSUE_LABELS" | jq -e 'index("agent-ready-for-dev")' >/dev/null || PR_OK=false

if [ "$PR_OK" != "true" ]; then
  # One repair attempt -- re-run the idempotent guarantee steps rather than
  # silently reporting success on a half-finished handoff.
  .claude/skills/oneshot/ensure_pr_linked.sh "$BRANCH" "$ISSUE_ID" || true
  if [ -n "${USE_GH_API:-}" ]; then
    .claude/skills/_lib/gh_api.sh issue-edit "$ISSUE_ID" --remove-label agent-planning --add-label agent-ready-for-dev 2>/dev/null || true
  else
    gh issue edit "$ISSUE_ID" --remove-label agent-planning --add-label agent-ready-for-dev 2>/dev/null || true
  fi
  # Re-check once; if still failing, this is what gets reported in step 10 --
  # never claim "ready for implementing" while any of the above is still false.
fi
```

   Treat a still-failing check after the repair attempt as the outcome of
   this run (see step 10), not as a background problem to ignore.

10. Report: issue number, PR URL, "ready for implementing" (only if step 9's
    checklist actually passed -- otherwise report exactly which guarantee
    is still unmet) -- and stop. Do not proceed to any developer/review
    work; that only happens inside `/implement-next-task`.

## If something looks wrong

If `find_candidate.sh` keeps returning the same `stale-reclaim` candidate
across multiple invocations without ever completing, the planning
orchestrator itself may be failing on this specific issue (not just
running slow) -- check `artifacts/feat-{issue_number}/state.json` in the
issue's branch for which phase is stuck, same as debugging `/oneshot`
today.
