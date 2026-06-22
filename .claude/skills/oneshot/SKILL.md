---
name: oneshot
description: Start the autonomous development pipeline for a feature that has been brainstormed and uploaded. Use when the user says "oneshot", "implement", "start pipeline", "run", or provides a feature ID after a brainstorm session.
---

You start the AgentHarness autonomous pipeline for a given feature.

This skill **always works inside a dedicated git worktree**. Never run the
implementation against the main checkout — isolate the work so the user's
primary working tree stays clean.

## Naming convention

Both the worktree directory and the branch it tracks **must** start with the
`feature/` prefix. Derive the suffix from the feature ID, e.g.:

- branch: `feature/feat-20260425-abc123`
- worktree dir: `../worktrees/feature-feat-20260425-abc123` (or any path whose
  basename starts with `feature-`)

## What you do

1. Check that the user provided a feature ID (e.g. `feat-20260425-abc123`). If not, ask for it.

2. Optionally show the uploaded brief so the user can confirm before starting:
```bash
agentharness status {feature_id}
```

3. **Mark the issue as work-in-progress.** Using the `gh` CLI, add the
   `agent-wip` label to the feature's GitHub issue and remove the `agent` label
   if it is present:
```bash
gh issue edit {issue_number} --add-label agent-wip --remove-label agent
```
   If the issue has no `agent` label, the `--remove-label` is a harmless no-op;
   keep `--add-label agent-wip` regardless.

4. Create and enter a dedicated worktree on a `feature/`-prefixed branch:
```bash
BRANCH="feature/{feature_id}"
WORKTREE="../worktrees/feature-{feature_id}"
git worktree add -b "$BRANCH" "$WORKTREE"
cd "$WORKTREE"
```
If the branch already exists, attach to it instead:
```bash
git worktree add "$WORKTREE" "$BRANCH"
```

5. Start the pipeline from inside the worktree. There is **no** `agentharness
   implement` command — the pipeline is driven by the `oneshot` orchestrator
   agent (`.claude/agents/oneshot.md`, installed by `agentharness init`). Follow
   that orchestrator end to end: it runs `agentharness checkpoint init
   {issue_number}` and then drives analyst → architect → designer → planner →
   developer(s) → reviewer via the Task tool, using `agentharness checkpoint`
   to track phase/task state.

6. Tell the user:
- The pipeline is now running autonomously inside the `feature/` worktree
- They can monitor it with `agentharness watch`
- The sequence: planner → architect → designer → developer(s) → reviewer
- If review fails, developer tasks are automatically retried (up to 3 rounds)
- They'll see the final result in `agentharness watch` when status changes to `done`

## Finishing the work

Once the implementation is complete, **from inside the worktree**:

1. **Test** the code. Run the project's test suite (and any linters) and make
   sure it passes before going further:
```bash
.venv/bin/pytest tests/ -v
```
   If tests fail, fix the issues (or report them) before committing — do not
   push a broken build.

2. **Commit** everything, including **all generated artifacts** (the `.md`
   files: brief, spec, arch-review, design, task-plan, impl, review). Stage the
   whole worktree so no artifact is left behind:
```bash
git add -A
git commit -m "@claude implement {feature_id}"
```
   The commit message **must** contain `@claude`.

3. **Push** the branch:
```bash
git push -u origin "feature/{feature_id}"
```
   If the push fails due to a network error, retry up to 4 times with
   exponential backoff (2s, 4s, 8s, 16s).

4. **Create a pull request** with an implementation summary, and tag it with the
   `agent` label. The summary must clearly state:
   - **What the issue / feature was** — the problem or request being addressed.
   - **How it was fixed / handled** — the approach taken and the key changes.

   Open the PR (base = the repository default branch, head =
   `feature/{feature_id}`) and add the `agent` label to it:
```bash
gh pr create \
  --base master \
  --head "feature/{feature_id}" \
  --label agent \
  --title "{feature_id}: implementation" \
  --body "$(cat <<'EOF'
## What the issue was
<description of the feature/problem from the brief>

## How it was fixed / handled
<summary of the approach and the main changes>

## Artifacts
- Brief, spec, design, task plan, impl, and review markdown are committed in this branch.
EOF
)"
```

5. **Mark the issue completed.** Using the `gh` CLI, remove the `agent-wip`
   label and add the `agent-completed` label to the feature's issue:
```bash
gh issue edit {issue_number} --remove-label agent-wip --add-label agent-completed
```

## If something looks wrong

If the user wants to adjust the brief before starting, remind them the brief is at:
```
artifacts/{feature_id}/brief.md
```
in the configured storage backend. They can download, edit, and re-upload it before calling implement.
