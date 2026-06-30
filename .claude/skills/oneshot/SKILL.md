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
git commit --allow-empty -m "implement feat-{issue_id}"
```
   Use `--allow-empty` so this commit always becomes `HEAD`, capturing any
   remaining artifacts. The whole-branch code review already ran inside the
   pipeline (see the orchestrator's Code Review phase), so no external review
   trigger is needed.

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

   **The PR body MUST link the tracking issue with a `Closes #{issue_id}`
   line** (using the GitHub issue number from `ISSUE_ID`). This is mandatory —
   it is what makes GitHub auto-close the issue when the PR merges. Never open a
   feature PR without it.

   Open the PR (base = the repository default branch, head = `$BRANCH`). Capture
   the PR URL, then run `.claude/skills/oneshot/ensure_pr_linked.sh "$PR_URL" "{issue_id}"`
   — this script ships beside this skill, so it is always present wherever the skill
   is installed. It is the guarantee for **both** requirements above. Do not rely on `--label` or the
   `Closes` template line on `gh pr create` alone; the LLM-filled body and the
   `--label` flag are both sometimes dropped. The script adds the `agent` label,
   injects `Closes #{issue_id}` if missing, then hard-fails if it cannot confirm
   either.

   First capture the pipeline's final code review so it can be surfaced on the PR.
   The `## Code review` section carries the whole-branch review run inside the
   pipeline — advisory cleanups and any unresolved correctness findings:
```bash
# Most recent code-review artifact (highest revision), if any.
REVIEW_FILE=$(ls -1 artifacts/feat-{issue_id}/code-review.r*.md 2>/dev/null | sort -V | tail -n1)
REVIEW_SECTION=""
if [ -n "$REVIEW_FILE" ]; then
  REVIEW_SECTION=$(printf '\n## Code review\n\n%s\n' "$(cat "$REVIEW_FILE")")
fi

PR_URL=$(gh pr create \
  --base master \
  --head "$BRANCH" \
  --label agent \
  --title "#{issue_id}: implementation" \
  --body "$(cat <<EOF
Closes #{issue_id}

## What the issue was
<description of the feature/problem from the brief>

## How it was fixed / handled
<summary of the approach and the main changes>

## Artifacts
- Brief, spec, design, task plan, impl, and review markdown are committed in this branch.
${REVIEW_SECTION}
EOF
)")

# MANDATORY: guarantee the `agent` label AND the `Closes #{issue_id}` link.
# Auto-repairs both if the agent dropped them, then hard-fails if it cannot.
.claude/skills/oneshot/ensure_pr_linked.sh "$PR_URL" "{issue_id}"
```
   Substitute the real GitHub issue number for `{issue_id}` in both the title
   and the `Closes #{issue_id}` line (same number used for `ISSUE_ID` above).

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
