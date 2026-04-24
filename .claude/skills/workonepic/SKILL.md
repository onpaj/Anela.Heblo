---
name: workonepic
description: Use when the user says "work on epic", "pick up epic task", "workonepic", or provides an epic issue number to work on. Finds the oldest open subtask of the epic tagged "agent", creates a branch from the epic's branch, backmerges main, implements the task, validates against spec, runs all tests, commits with @claude marker, pushes, creates a PR to the epic branch, and marks the subtask as solved.
---

# Work on Epic — Pick Up and Implement Next Agent Subtask

End-to-end workflow for picking up the oldest open "agent"-tagged subtask of a given epic, implementing it, and delivering a PR back to the epic branch.

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

### 4. Create a feature branch from the epic branch

```bash
git checkout <epic-branch>
git pull origin <epic-branch>
git checkout -b feat/<subtask-number>-<kebab-slug-from-title>
```

Branch naming: always prefix with `feat/` and include the subtask issue number.

### 5. Backmerge main into the new branch

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

Invoke `superpowers:subagent-driven-development` skill to carry out the plan. **Do not implement manually** — always delegate to subagents.

### 10. Validate against the specification

After implementation, re-read the subtask body and verify every acceptance criterion is met:
- All described behaviour is implemented
- No required fields, endpoints, or UI elements are missing
- Edge cases mentioned in the spec are handled

If anything is missing, fix it before proceeding.

### 11. Run all tests (hard gate)

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

### 12. Commit, push, and open a PR

Stage all changes and create a conventional commit with `@claude` in the body:

```
feat: <description of what was implemented>

@claude
```

Push and create a PR — **target the epic branch**, not `main`:

```bash
git push -u origin <feature-branch>
gh pr create --base <epic-branch> \
  --title "feat: <description>" \
  --body "<summary of changes>\n\nCloses #<subtask-number>"
```

### 13. Mark subtask as solved — swap label "agent-wip" → "agent-solved"

```bash
gh issue edit <subtask-number> --remove-label "agent-wip" --add-label "agent-solved"
```

## Hard Gates

- **ALWAYS** create a new branch from the epic branch — never work directly on the epic branch or on `main`
- **ALWAYS** backmerge `main` before starting implementation
- **ALWAYS** validate the implementation against the full subtask spec before running tests
- **ALWAYS** use subagent-driven development for implementation
- All BE and FE tests (excluding E2E) **MUST** pass before pushing
- The commit message **MUST** contain `@claude`
- The PR **MUST** target the epic branch, not `main`
- The subtask label **MUST** be updated at both the start (`agent` → `agent-wip`) and end (`agent-wip` → `agent-solved`)
