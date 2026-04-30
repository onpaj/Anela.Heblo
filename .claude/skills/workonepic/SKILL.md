---
name: workonepic
description: Use when the user says "work on epic", "pick up epic task", "workonepic", or provides an epic issue number to work on. Finds the oldest open subtask of the epic tagged "agent", checks out the epic branch, backmerges main, implements the task directly on the epic branch, validates against spec, runs all tests, commits with @claude marker, pushes, and marks the subtask as solved.
---

# Work on Epic — Pick Up and Implement Next Agent Subtask

End-to-end workflow for picking up the oldest open "agent"-tagged subtask of a given epic, implementing it directly on the epic branch inside a worktree, and pushing the result.

## Usage

```
/workonepic 42    # work on the next agent-tagged subtask of epic #42
```

`$ARGUMENTS` contains the epic issue number.

## Steps

### 1. Load the epic

```bash
gh issue view $ARGUMENTS --json number,title,body,labels,assignees
```

Identify the **epic branch** — the branch the epic is being developed on. Look for a branch whose name contains the epic number:

```bash
git fetch origin
git branch -a | grep "$ARGUMENTS"
```

### 2. Find the oldest open "agent"-tagged subtask

List all open issues carrying the label `agent`:

```bash
gh issue list --label "agent" --state open --json number,title,labels,createdAt \
  | jq 'sort_by(.createdAt)'
```

Cross-reference with the epic's task list to ensure the candidate belongs to this epic. The epic body contains a GitHub task list like `- [ ] #NNN`:

```bash
gh issue view $ARGUMENTS --json body | jq -r '.body' | grep -oP '(?<=- \[ \] #)\d+'
```

Pick the **oldest** (earliest `createdAt` / lowest number) open subtask that:
- Has label `agent`
- Is **not** closed
- Appears in the epic's task list

**STOP immediately if no candidate is found.** Do not proceed to step 3. Report the reason clearly:

| Situation | Message to user |
|-----------|----------------|
| Epic has no open subtasks at all | "Epic #N has no open subtasks remaining. All work may be complete." |
| Open subtasks exist but none have label `agent` | "Epic #N has open subtasks but none are labelled `agent`. Label a subtask `agent` to queue it for automated work." |
| `agent`-labelled issues exist but none belong to this epic | "No `agent`-labelled issues found in epic #N's task list. Verify the subtasks are linked in the epic body." |

### 3. Claim the subtask — swap label "agent" → "agent-wip"

```bash
gh issue edit <subtask-number> --remove-label "agent" --add-label "agent-wip"
```

### 4. Set up a git worktree for the epic branch

Invoke `superpowers:using-git-worktrees` to create an isolated worktree checked out to the epic branch. This keeps the working directory clean and avoids disturbing the main checkout.

```bash
git fetch origin
git worktree add ../worktrees/<epic-branch-slug> <epic-branch>
cd ../worktrees/<epic-branch-slug>
git pull origin <epic-branch>
```

All work for this subtask happens directly on the epic branch inside this worktree — do **not** create a separate feature branch.

### 5. Backmerge main into the epic branch

```bash
git fetch origin main
git merge origin/main --no-edit
```

Resolve any merge conflicts before continuing.

### 6. Load the subtask specification

```bash
gh issue view <subtask-number> --json number,title,body,labels,comments
```

Read the full body and any clarifying comments. This is the authoritative specification.

### 7. Brainstorm the design

Invoke `superpowers:brainstorming` skill with the subtask content as context. Ensure all requirements are understood and the design approach is clear before writing any code.

### 8. Write an implementation plan

Invoke `superpowers:writing-plans` skill to create a detailed, step-by-step plan from the brainstormed design.

### 9. Implement with subagents

**MANDATORY:** Invoke `superpowers:subagent-driven-development` skill to carry out the plan. **Do not implement manually** — always delegate to subagents. This is non-negotiable.

Each task in the plan **MUST** include writing tests as part of the task itself — not as a follow-up step. Every implemented feature or change requires:
- **Backend tests** (unit or integration) covering the new/changed .NET code
- **Frontend tests** (Jest + React Testing Library) covering the new/changed React code

A task is **not complete** until both its implementation and its tests are written and passing.

### 10. Code review (hard gate after each task)

After each task is implemented (including its tests), invoke the `code-reviewer` agent to validate the result:

```
Use the code-reviewer agent to review the implementation of [task description].
Verify: code quality, test coverage for both backend and frontend, alignment with spec, no security issues.
```

**STOP if the code-reviewer flags any CRITICAL or HIGH issues.** Fix them before proceeding to the next task.

### 11. Validate against the specification

After all tasks are complete, re-read the subtask body and verify every acceptance criterion is met:
- All described behaviour is implemented
- No required fields, endpoints, or UI elements are missing
- Edge cases mentioned in the spec are handled

If anything is missing, fix it before proceeding.

### 12. Run all tests (hard gate)

Run backend tests:
```bash
dotnet test backend/
```

Run frontend tests:
```bash
cd frontend && npm test -- --watchAll=false
```

Run linters:
```bash
dotnet format --verify-no-changes
cd frontend && npm run lint
```

**STOP if any test or lint check fails.** Fix failures first, then re-run. E2E tests are **not** required here.

### 13. Commit and push to the epic branch

Stage all changes and create a conventional commit with `@claude` in the body:

```
feat: <description of what was implemented>

Implements #<subtask-number>

@claude
```

Push directly to the epic branch — no separate PR is needed per subtask:

```bash
git push origin <epic-branch>
```

### 14. Mark subtask as solved — swap label "agent-wip" → "agent-solved" and close the issue

```bash
gh issue edit <subtask-number> --remove-label "agent-wip" --add-label "agent-solved"
gh issue close <subtask-number>
```

### 15. If this was the last subtask — mark the epic as "agent-solved"

Check whether all subtasks in the epic's task list are now closed:

```bash
# Get all subtask numbers from the epic body
gh issue view $ARGUMENTS --json body | jq -r '.body' | grep -oP '(?<=- \[[ x]\] #)\d+'

# Check state of each subtask
for num in <subtask-numbers>; do
  gh issue view $num --json state,number | jq '{number, state}'
done
```

If **every** subtask is in `CLOSED` state, add `agent-solved` to the epic:

```bash
gh issue edit $ARGUMENTS --add-label "agent-solved"
```

Do **not** close the epic issue itself — only add the label. The epic may be closed manually by the team after final review.

## Hard Gates

- **ALWAYS** use a git worktree — invoke `superpowers:using-git-worktrees` to set up a worktree for the epic branch before doing any implementation work
- **ALWAYS** work directly on the epic branch — never create a per-subtask feature branch
- **NEVER** work on `main` directly
- **ALWAYS** backmerge `main` into the epic branch before starting implementation
- **ALWAYS** validate the implementation against the full subtask spec before running tests
- **ALWAYS** use `superpowers:subagent-driven-development` for implementation — never implement manually
- **EVERY** task **MUST** include both backend (.NET) AND frontend (Jest/RTL) tests — implementation without tests is not complete
- **EVERY** task **MUST** be reviewed by the `code-reviewer` agent before moving to the next task — CRITICAL/HIGH issues must be fixed
- All BE and FE tests (excluding E2E) **MUST** pass before pushing
- The commit message **MUST** contain `@claude`
- Changes are pushed directly to the epic branch — **no per-subtask PR** is created
- The subtask label **MUST** be updated at both the start (`agent` → `agent-wip`) and end (`agent-wip` → `agent-solved`)
- The subtask issue **MUST** be closed (`gh issue close`) after the label is updated to `agent-solved`
- After closing the subtask, **ALWAYS** check if all epic subtasks are now closed — if they are, add `agent-solved` to the epic issue (do not close the epic itself)
