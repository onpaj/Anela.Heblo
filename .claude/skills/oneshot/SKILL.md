---
name: oneshot
description: Start the autonomous development pipeline for a feature that has been brainstormed and uploaded. Use when the user says "oneshot", "implement", "start pipeline", "run", or provides a feature ID after a brainstorm session.
---

You start the AgentHarness autonomous pipeline for a given feature.

This skill **always works inside a dedicated git worktree**. Never run the
implementation against the main checkout — isolate the work so the user's
primary working tree stays clean.

## Naming convention

Both the worktree directory and the branch it tracks **must** use the strict,
deterministic form `feature/{issue_id}-{Title-Slug}`, where:

- `{issue_id}` is the GitHub issue **number** only (e.g. `9863`). Never the
  `feat-…` feature id, and never any other prefix.
- `{Title-Slug}` is the issue **title** rendered as Title-Case words joined by
  single hyphens: apostrophes stripped, every other non-alphanumeric run
  collapsed to a hyphen, each word capitalized, truncated to ~50 chars.

So for issue #9863 titled "What's This About?":

- branch: `feature/9863-Whats-This-About`
- worktree dir: `../worktrees/feature-9863-Whats-This-About` (basename
  starts with `feature-`)

Derive the slug **only** with this exact `gh` + `awk` pipeline so the name is
always identical for the same title — do not improvise the slug:
```bash
ISSUE_ID={issue_number}
SLUG=$(gh issue view "$ISSUE_ID" --json title --jq '.title' \
  | sed -E "s/['’]//g" \
  | sed -E 's/[^A-Za-z0-9]+/ /g' \
  | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2)); print}' \
  | sed -E 's/ +/-/g; s/^-+|-+$//g' \
  | cut -c1-50 | sed -E 's/-+$//')
BRANCH="feature/${ISSUE_ID}-${SLUG}"
WORKTREE="../worktrees/feature-${ISSUE_ID}-${SLUG}"
```

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

4. Create and enter a dedicated worktree on the `feature/{issue_id}-{Title-Slug}`
   branch (compute `BRANCH` and `WORKTREE` as shown in **Naming convention**):
```bash
git worktree add -b "$BRANCH" "$WORKTREE"
cd "$WORKTREE"
```
If the branch already exists, attach to it instead:
```bash
git worktree add "$WORKTREE" "$BRANCH"
```

5. Start the pipeline from inside the worktree. There is **no** `agentharness
   implement` command — the pipeline is driven by the `orchestrator`
   agent (`.claude/agents/orchestrator.md`, installed by `agentharness init`). Follow
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

2. **Commit** everything, including **all generated artifacts** committed under
   the `artifacts/` folder, exactly the way the previous harness laid them out.
   The pipeline writes every artifact to `artifacts/feat-{issue_id}/`:
```
artifacts/feat-{issue_id}/brief.md            # the issue brief
artifacts/feat-{issue_id}/spec.r1.md          # analyst output
artifacts/feat-{issue_id}/arch-review.r1.md   # architect output
artifacts/feat-{issue_id}/design.r1.md        # designer output
artifacts/feat-{issue_id}/task-plan.r1.md     # planner output
artifacts/feat-{issue_id}/impl/{task}.rN.md   # developer output per task/revision
artifacts/feat-{issue_id}/review/{task}.rN.md # reviewer output per task/revision
artifacts/feat-{issue_id}/state.json          # checkpoint state
```
   Make sure this whole `artifacts/feat-{issue_id}/` tree is staged (the `.md`
   files must end up in the commit, not just the code), then stage the rest of
   the worktree so nothing is left behind:
```bash
git add -A artifacts/feat-{issue_id}    # ensure all generated .md artifacts are staged
git add -A                              # stage code + everything else
git commit --allow-empty -m "implement feat-{issue_id} @claude"
```
   The commit message **must end with** `@claude` (the trigger phrase goes at the
   very end of the message, not the start). Use `--allow-empty` so this marker
   commit always becomes `HEAD` — even when the orchestrator already committed
   every artifact — otherwise the `@claude` trigger never reaches the tip of the
   branch and the GitHub Claude review is not invoked.

3. **Push** the branch:
```bash
git push -u origin "$BRANCH"
```
   If the push fails due to a network error, retry up to 4 times with
   exponential backoff (2s, 4s, 8s, 16s).

4. **Create a pull request** with an implementation summary, and tag it with the
   `agent` label. The summary must clearly state:
   - **What the issue / feature was** — the problem or request being addressed.
   - **How it was fixed / handled** — the approach taken and the key changes.

   Open the PR (base = the repository default branch, head = `$BRANCH`) and add
   the `agent` label to it:
```bash
gh pr create \
  --base master \
  --head "$BRANCH" \
  --label agent \
  --title "#{issue_id}: implementation" \
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
