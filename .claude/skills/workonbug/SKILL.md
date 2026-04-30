---
name: workonbug
description: Use when the user says "work on bug", "fix a bug", "workonbug", or wants to autonomously pick up and fix a bug issue. Finds the oldest open GitHub issue labelled both "agent" and "bug", creates a worktree from origin/main, implements the fix using subagent-driven development, validates all tests, commits with @claude marker, pushes, creates a PR to main, and cleans up the worktree.
---

# Work on Bug — Pick Up and Fix the Oldest Agent Bug

End-to-end autonomous workflow for finding the oldest open bug labelled `agent` + `bug`, fixing it in a fresh worktree branched from `main`, validating both backends pass, and shipping a PR.

## Steps

### 1. Find the oldest open "agent" + "bug" issue

```bash
gh issue list --label "agent" --label "bug" --state open \
  --json number,title,labels,createdAt \
  | jq 'sort_by(.createdAt)'
```

Pick the **oldest** issue (lowest `createdAt` / lowest number) that:
- Is **open**
- Has both labels `agent` **and** `bug`
- Does **not** already have an open PR linked to it

To check for an existing PR, scan open PRs for a reference to the issue number:
```bash
gh pr list --state open --json number,title,body,headRefName \
  | jq '.[] | select(.body | test("#<issue-number>"))'
```

**STOP immediately if no candidate is found.** Report clearly:

| Situation | Message |
|-----------|---------|
| No open issues with both `agent` and `bug` labels | "No open issues found with both 'agent' and 'bug' labels. Nothing to work on." |
| Matching issues exist but all already have an open PR | "All matching bug issues already have an open PR. Nothing new to start." |

### 2. Claim the issue — swap label "agent" → "agent-wip"

```bash
gh issue edit <issue-number> --remove-label "agent" --add-label "agent-wip"
```

This prevents another agent from picking up the same issue concurrently.

### 3. Load the full issue specification

```bash
gh issue view <issue-number> --json number,title,body,labels,comments
```

Read the full body and all comments. This is the authoritative bug report and acceptance criteria.

### 4. Create a git worktree branched from origin/main

Invoke `superpowers:using-git-worktrees` to create an isolated worktree. The branch name must encode the issue number so it is easy to find:

```bash
git fetch origin
git worktree add ../worktrees/fix-<issue-number>-<short-slug> -b fix/<issue-number>-<short-slug> origin/main
```

All implementation work happens inside this worktree — **never touch the main checkout**.

### 5. Brainstorm the fix

Invoke `superpowers:brainstorming` skill with the full issue body as context. Understand:
- Root cause (as described or inferable)
- Minimal reproduction steps
- Expected vs. actual behaviour
- Edge cases and regression risks
- What tests should be added/fixed to prevent recurrence

### 6. Write an implementation plan

Invoke `superpowers:writing-plans` skill to create a detailed, step-by-step fix plan from the brainstorm. The plan must include:
- Files to change
- Tests to add or update (TDD: write test first, then fix)
- Acceptance criteria checklist

### 7. Implement the fix with subagents

Invoke `superpowers:subagent-driven-development` skill to carry out the plan. **Do not implement manually** — always delegate to subagents.

Follow TDD:
1. Write a failing test that reproduces the bug (RED)
2. Fix the bug so the test passes (GREEN)
3. Refactor if needed (IMPROVE)

### 8. Validate against the specification

After implementation, re-read the issue body and every comment. Verify:
- The described bug is no longer reproducible
- All acceptance criteria are satisfied
- No related edge cases were missed

Fix anything missing before continuing.

### 9. Build both backends (hard gate)

```bash
# Backend
dotnet build backend/

# Frontend
cd frontend && npm run build
```

**STOP if either build fails.** Fix the error and re-run before continuing.

`npm run build` runs `tsc` and catches TypeScript errors that Jest/Babel miss — this is mandatory.

### 10. Run all tests (hard gate)

```bash
# Backend
dotnet test backend/

# Frontend
cd frontend && CI=true npm test -- --no-coverage --watchAll=false
```

Run linters:
```bash
dotnet format --verify-no-changes
cd frontend && npm run lint
```

**STOP if any test or lint check fails.** Fix, re-run builds, and re-run tests. E2E tests are **not** required here.

### 11. Commit with @claude marker

Stage all changes and commit with a conventional commit message. The commit body **must** contain `@claude` to trigger automated code review:

```
fix: <concise description of what was fixed>

Fixes #<issue-number>

@claude
```

`Fixes #<issue-number>` causes GitHub to auto-close the issue when the PR is merged.

### 12. Push the branch and create a PR to main

```bash
git push origin fix/<issue-number>-<short-slug>
```

Create the PR:
```bash
gh pr create \
  --title "fix: <concise description>" \
  --body "$(cat <<'EOF'
## Summary

<1–3 bullet points describing the root cause and the fix>

## Test plan

- [ ] Bug is no longer reproducible via the steps in #<issue-number>
- [ ] New regression test added
- [ ] All BE tests pass (`dotnet test`)
- [ ] All FE tests pass (`npm test`)
- [ ] Both builds succeed (`dotnet build` + `npm run build`)

Fixes #<issue-number>
EOF
)" \
  --base main \
  --label "agent"
```

### 13. Mark issue as solved — swap label "agent-wip" → "agent-solved"

```bash
gh issue edit <issue-number> --remove-label "agent-wip" --add-label "agent-solved"
```

### 14. Delete the worktree

```bash
cd <repo-root>
git worktree remove ../worktrees/fix-<issue-number>-<short-slug> --force
```

## Hard Gates

- **ALWAYS** use a git worktree — never commit directly in the main checkout
- **ALWAYS** branch from `origin/main` — not from the current branch
- **NEVER** work on `main` directly
- **ALWAYS** brainstorm before writing a plan; write a plan before writing code
- **ALWAYS** use subagent-driven development for implementation
- **ALWAYS** run `npm run build` (FE) and `dotnet build` (BE) before declaring completion — they catch TypeScript errors that tests miss
- All BE and FE tests (excluding E2E) **MUST** pass before pushing
- The commit message **MUST** contain `@claude`
- A PR **MUST** be created targeting `main` with `Fixes #<issue-number>` in the body
- The worktree **MUST** be deleted after the PR is created
- Issue labels **MUST** be updated at both the start (`agent` → `agent-wip`) and end (`agent-wip` → `agent-solved`)
