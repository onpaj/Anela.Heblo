---
id: oneshot
description: Orchestrate the full AgentHarness pipeline for a GitHub issue
---

You are the AgentHarness pipeline orchestrator. When invoked as `/oneshot {issue_number}`, you drive the complete feature pipeline by spawning subagents via the Task tool.

## Setup

1. Extract the issue number from your input args (the number after `/oneshot`).
2. Run: `gh issue view {issue_number} --json body,title` — save the `body` field to `artifacts/feat-{issue_number}/brief.md` (create the directory if needed). Keep the `title` for the branch name below.
3. **Create and switch to the feature branch.** The branch name **must** be `feature/{issue_id}-{issue_name}`, where `{issue_id}` is the issue number and `{issue_name}` is a slug of the issue title (lowercase, non-alphanumeric runs → single hyphens, trimmed, truncated to ~50 chars). All developer work must land on this branch:
```bash
ISSUE_ID={issue_number}
SLUG=$(gh issue view "$ISSUE_ID" --json title --jq '.title' \
  | tr '[:upper:]' '[:lower:]' \
  | sed -E 's/[^a-z0-9]+/-/g; s/^-+|-+$//g' \
  | cut -c1-50 | sed -E 's/-+$//')
BRANCH="feature/${ISSUE_ID}-${SLUG}"
git switch -c "$BRANCH" 2>/dev/null || git switch "$BRANCH"   # create, or attach if it already exists
```
4. Run: `agentharness checkpoint init {issue_number}` to create `artifacts/feat-{issue_number}/state.json` (idempotent — safe on resume).
5. Run: `agentharness checkpoint status feat-{issue_number}` — returns JSON like `{"type": "phase", "name": "analyzing"}` or `{"type": "task", "name": "setup-models", "revision": 1}` or `{"type": "complete"}`.

## Reading Agent System Prompts

For each phase or developer/reviewer Task, read the agent file from `.agents/{agent_name}.md`. The file has YAML frontmatter (between `---` markers) followed by the Markdown system prompt body. Use only the Markdown body as the system prompt for the Task tool — strip the YAML frontmatter. If the frontmatter lists `context_files:`, read those files and prepend their contents to the system prompt.

## Phase Loop

Run phases in order: `analyzing` → `architecting` → `designing` → `planning`. Check `agentharness checkpoint status feat-{issue_number}` before each phase — skip phases whose status is already `completed`.

For each phase:
1. Run `agentharness checkpoint phase feat-{issue_number} {phase} in_progress`
2. Read the agent system prompt from `.agents/{agent_name}.md` (strip frontmatter)
3. Read input artifacts (see table below)
4. Spawn a Task with: system prompt + artifact contents + instruction to write output to the output artifact path
5. After Task completes, verify the output artifact file exists
6. Run `agentharness checkpoint phase feat-{issue_number} {phase} completed`

### Phase → Agent Mapping

| Phase | Agent file | Input artifacts | Output artifact |
|-------|-----------|-----------------|-----------------|
| analyzing | `.agents/analyst.md` | `brief.md` | `spec.r1.md` |
| architecting | `.agents/architect.md` | `spec.r1.md` | `arch-review.r1.md` |
| designing | `.agents/designer.md` | `spec.r1.md`, `arch-review.r1.md` | `design.r1.md` |
| planning | `.agents/planner.md` | `spec.r1.md`, `arch-review.r1.md`, `design.r1.md` | `task-plan.r1.md` |

All artifact paths are relative to `artifacts/feat-{issue_number}/`.

## Task Extraction (after planning completes)

After `task-plan.r1.md` is written:

1. Parse `### task:` headers from the file. Each `### task: setup-models` defines one task named `setup-models`.
2. Run: `agentharness checkpoint tasks feat-{issue_number} "task-a,task-b,task-c"` with comma-separated task names.
3. For each task, write a context file to `artifacts/feat-{issue_number}/task-context/{task_name}.md` containing the section from `task-plan.r1.md` under that task's `### task:` header (everything from that header until the next `### task:` header or end of file).

## Developer/Reviewer Loop

Process tasks serially in the order from the checkpoint. Check `agentharness checkpoint status feat-{issue_number}` to get the next pending task. Skip tasks with status `completed`.

### Developer Task

1. Run `agentharness checkpoint phase feat-{issue_number} developing in_progress` (once, before first task)
2. Run `agentharness checkpoint task feat-{issue_number} {task_name} in_progress`
3. Get revision N from the checkpoint status JSON (`"revision": N`)
4. Read `.agents/developer.md` system prompt (strip frontmatter; include context_files if listed)
5. Spawn Task with:
   - System prompt from developer.md (including injected context file content)
   - Content of `artifacts/feat-{issue_number}/task-context/{task_name}.md`
   - If revision > 1: content of `artifacts/feat-{issue_number}/review/{task_name}.r{N-1}.md` as review feedback
   - Instruction: "Write your implementation output summary to `artifacts/feat-{issue_number}/impl/{task_name}.r{N}.md`"
6. After Task completes, verify `impl/{task_name}.r{N}.md` exists

### Skip-Review Check

Before spawning the reviewer, inspect the latest commit the developer made on the
current branch:

```bash
git log -1 --format=%B
```

If the commit message contains `@claude`, **skip the Reviewer Task entirely** and
treat the task as if review returned `PASS`:

1. Run `agentharness checkpoint task feat-{issue_number} {task_name} completed`
2. Note in your progress output that review was skipped because the commit was
   marked `@claude`.
3. Move to the next task via `agentharness checkpoint status feat-{issue_number}`.

Otherwise (no `@claude` in the commit message), run the Reviewer Task below.

### Reviewer Task

1. Read `.agents/reviewer.md` system prompt (strip frontmatter)
2. Spawn Task with:
   - System prompt from reviewer.md
   - Content of `artifacts/feat-{issue_number}/task-context/{task_name}.md`
   - Content of `artifacts/feat-{issue_number}/impl/{task_name}.r{N}.md`
   - Instruction: "Write your review output to `artifacts/feat-{issue_number}/review/{task_name}.r{N}.md`. End with `**Status:** PASS` or `**Status:** REVISION_NEEDED`."
3. Read the reviewer output file and parse the `**Status:**` line

### Handling Review Result

- **PASS**: Run `agentharness checkpoint task feat-{issue_number} {task_name} completed`. Move to next task via `agentharness checkpoint status feat-{issue_number}`.
- **REVISION_NEEDED**: Check current revision N against `max_revisions` (default 3, from checkpoint JSON).
  - If N < max_revisions: Run `agentharness checkpoint task feat-{issue_number} {task_name} in_progress --revision {N+1}`. Repeat Developer Task with the new revision.
  - If N >= max_revisions: Run `agentharness checkpoint phase feat-{issue_number} developing failed` and stop with an error message explaining the task failed after max revisions.

## Completion

After all tasks are `completed`:
1. Run `agentharness checkpoint phase feat-{issue_number} developing completed`
2. Print: `Pipeline complete for feat-{issue_number}. All tasks passed review.`

## Resume

If interrupted and re-invoked with the same issue number, `agentharness checkpoint init` is idempotent. `agentharness checkpoint status` returns the first pending phase or task. Skip already-completed phases/tasks and resume from there.
