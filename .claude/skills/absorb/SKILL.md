---
name: absorb
description: Absorb an existing GitHub PR into the current Conductor workspace. Use when the user says "absorb PR", "absorb PR 123", "load PR", "take over PR", "work on PR", or wants to continue work on a pull request that was created outside this workspace. Fetches the PR branch, checks it out, syncs with remote, backmerges the base branch (main), resolves any merge conflicts, runs tests, and fixes failing tests — leaving the workspace ready to continue work.
---

# Absorb PR

Loads an existing GitHub PR into this Conductor workspace, fully reconciled and test-passing: fetches the branch, syncs it, backmerges the base branch, resolves conflicts, runs tests, fixes failures, and writes a `.context/pr.md` file.

## Usage

```
/absorb <PR_NUMBER>
```

## Steps

### 1. Safety check

```bash
git status --porcelain
```

If there are uncommitted changes, **stop** and ask the user to stash or commit them first.

### 2. Fetch PR metadata

```bash
gh pr view <PR_NUMBER> --json number,title,body,headRefName,baseRefName,url,state,author,additions,deletions,changedFiles
```

Store `headRefName` as `<branch>` and `baseRefName` as `<base>` (typically `main`).

### 3. Fetch and checkout the branch

```bash
git fetch origin
git checkout <branch> 2>/dev/null || git checkout -b <branch> origin/<branch>
git pull origin <branch>
```

### 4. Backmerge the base branch

```bash
git merge origin/<base> --no-edit
```

If it merges cleanly — proceed.

If there are conflicts:

1. List all conflicted files:
   ```bash
   git diff --name-only --diff-filter=U
   ```

2. For each conflicted file, read it and reason about the correct resolution:
   - Understand what the PR branch changed vs what the base branch changed.
   - Prefer the PR branch's intent — keep its logic, integrate base branch additions alongside.
   - For structural conflicts (both sides added to the same file), combine both changes.
   - For logic conflicts, use your understanding of the codebase to pick the correct version.

3. After editing each file to resolve it:
   ```bash
   git add <file>
   ```

4. Once all conflicts are resolved:
   ```bash
   git commit -m "chore: backmerge origin/<base> into <branch>"
   ```

### 5. Run tests

Detect the project type and run the full suite:

- **.NET backend** — `dotnet test`
- **Frontend** — `npm run build && npm run lint` (from `frontend/` directory)

If all tests pass, proceed to step 7.

### 6. Fix failing tests

For each failing test:

1. Read the failure message carefully.
2. Locate the relevant source file(s).
3. Determine whether the failure is in the test or in the implementation:
   - If the test is wrong (e.g. testing a removed API that no longer exists), update the test.
   - If the implementation is broken, fix the implementation.
4. Re-run only the affected test to confirm the fix.
5. Continue until all tests pass.

Commit fixes:
```bash
git commit -m "fix: resolve failing tests after backmerge with <base>"
```

### 7. Push the updated branch

```bash
git push origin <branch>
```

### 8. Write workspace context

```bash
mkdir -p .context
```

Create or overwrite `.context/pr.md`:

```markdown
# PR Context

- **PR**: #<number> — <title>
- **URL**: <url>
- **Branch**: `<branch>` → `<base>`
- **State**: <state>
- **Author**: <author.login>
- **Changes**: +<additions> / -<deletions> across <changedFiles> files
- **Absorbed**: backmerged with `<base>`, all tests passing

## Description

<body>
```

### 9. Report status

Print a short summary and stop — do not start implementing new features unless the user asks:

```
Absorbed PR #<number>: <title>
Branch: <branch> (backmerged with <base>, pushed)
Tests: all passing
Context written to .context/pr.md
```

## Edge Cases

- **PR merged or closed**: warn the user, but proceed — they may want to review or extend the branch.
- **Unresolvable conflicts**: if a conflict is genuinely ambiguous (logic collision in complex domain code), pause, show the conflict, and ask the user how to resolve it before continuing.
- **Tests failing beyond a simple fix**: if fixing tests requires understanding a major design change, describe what's failing and ask the user for guidance rather than guessing.
- **Branch already up to date with base**: skip the merge step and note it in the final summary.
