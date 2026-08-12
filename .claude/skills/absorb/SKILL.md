---
name: absorb
description: Absorb an existing GitHub PR into the current local checkout. Use when the user says "absorb PR", "absorb PR 123", "load PR", "take over PR", "work on PR", or wants to continue work on a pull request that was created outside this session. Fetches the PR branch, checks it out, backmerges the default branch, resolves any merge conflicts, runs the project's own test suite and fixes failures, checks the PR discussion for outstanding requested work, and leaves the checkout ready to continue work.
---

# Absorb PR

Loads an existing GitHub PR into this checkout, fully reconciled and
test-passing: fetches the branch, backmerges the default branch, resolves
conflicts, runs tests, fixes failures, surfaces any unresolved discussion
that still needs action, and writes a `.context/pr.md` file. Unlike
`/rework-pr`, this is not scoped to `agent`-labelled PRs or a revision
cap — it works on any PR, including ones a human opened by hand.

## Usage

```
/absorb <PR_NUMBER>
```

## 0. Resolve `$REPO` first

```bash
REPO="${GH_REPO:-$(git remote get-url origin | sed -e 's#.*github\.com[:/]##' -e 's#\.git$##')}"
```

If that does not produce an `owner/name` pair, stop and say so. Use
`--repo "$REPO"` on every `gh` call below.

## 1. Safety check

```bash
git status --porcelain
```

If there are uncommitted changes, **stop** and ask the user to stash or
commit them first — this skill checks out a different branch in place, in
the current checkout, not an isolated worktree.

## 2. Fetch PR metadata

```bash
gh pr view <PR_NUMBER> --repo "$REPO" \
  --json number,title,body,headRefName,baseRefName,url,state,author,additions,deletions,changedFiles
```

Store `headRefName` as `<branch>`. Do not assume `baseRefName` is `main` —
read it from this call, or cross-check against the actual default branch
in step 4.

If `.state` is not `OPEN`, warn the user (merged or closed PRs can still
be absorbed for review or follow-up work) and continue.

## 3. Fetch and check out the branch

```bash
git fetch origin <branch>
git checkout <branch> 2>/dev/null || git checkout -b <branch> origin/<branch>
git reset --hard origin/<branch>
```

## 4. Backmerge the default branch

Resolve the actual default branch rather than assuming `main`:

```bash
DEFAULT_BRANCH=$(gh repo view "$REPO" --json defaultBranchRef --jq .defaultBranchRef.name)
git fetch origin "$DEFAULT_BRANCH"
git merge "origin/$DEFAULT_BRANCH" --no-edit
```

Use `merge`, never `rebase` — this rewrites nothing, so the eventual push
stays a plain `git push`.

If it merges cleanly, proceed to step 5.

If there are conflicts:

1. List the conflicted files: `git diff --name-only --diff-filter=U`
2. For each one, read it and reason about the correct resolution — prefer
   the PR branch's intent, integrate the default branch's additions
   alongside it, and combine both sides on a structural conflict (both
   added to the same file).
3. `git add <file>` once each is resolved.
4. If a conflict's intent is genuinely unclear (the same line changed two
   incompatible ways for reasons you can't determine from context), **stop
   and ask the user** how to resolve it — do not guess on ambiguous logic
   collisions. Otherwise, once every file is resolved:

```bash
git commit -m "chore: backmerge origin/$DEFAULT_BRANCH into <branch>"
```

## 5. Run the project's own test suite

Detect how this repo runs its tests rather than assuming a stack — check,
in order: a project skill or `CLAUDE.md`/`AGENTS.md` section that already
documents the test command, then common manifests (`pyproject.toml` →
`pytest`, `package.json`'s `scripts.test` → `npm test`, `*.csproj`/`*.sln`
→ `dotnet test`, `Cargo.toml` → `cargo test`, `go.mod` → `go test ./...`).
If none of these resolve unambiguously, ask the user for the test command
rather than guessing.

If all tests pass, skip to step 7.

## 6. Fix failing tests

For each failure:

1. Read the failure message carefully.
2. Locate the relevant source file(s).
3. Determine whether the failure is in the test or the implementation — if
   the test targets a removed/changed API, update the test; if the
   implementation is actually broken, fix the implementation.
4. Re-run only the affected test to confirm the fix.
5. Continue until the full suite passes.

If a failure requires understanding a design change beyond a
straightforward fix, describe what's failing and ask the user for
guidance rather than guessing.

Commit fixes:

```bash
git commit -m "fix: resolve failing tests after backmerge with $DEFAULT_BRANCH"
```

## 7. Push the updated branch

```bash
git push origin <branch>
```

Skip this step (and note it in the final summary) if step 4 made no new
commits and step 6 wasn't needed — nothing changed to push.

## 8. Check the PR discussion for outstanding work

Gather the full discussion, not just the latest comment:

```bash
gh pr view <PR_NUMBER> --repo "$REPO" --json comments,reviews
gh api "repos/$REPO/pulls/<PR_NUMBER>/comments"
```

Read every comment and review against the current diff (`gh pr diff
<PR_NUMBER> --repo "$REPO"`) and judge, per item, whether it's already
addressed by what's on the branch now or still outstanding — a
`CHANGES_REQUESTED` review with no newer commit addressing it, an
unresolved inline review comment, or a plain comment asking for something
("can you also...", "please also fix...") that the diff doesn't reflect.

If nothing outstanding is found, note that in the final summary and move
to step 9.

If you find genuine outstanding items, **stop and list them for the
user** — do not act on them unprompted. For each: who asked, what they
asked for, and whether it looks small or substantial. Then ask whether
they want this work done now.

- If the user declines, or wants to handle it themselves: stop here,
  leave the branch as reconciled in steps 3–7.
- If the user agrees: for each confirmed item, spawn a `general-purpose`
  subagent (via the `Agent` tool) to implement it — give it the specific
  comment/review text, the file(s) it concerns, and this repo's normal
  test-before-commit expectations. Review what it changed, run the test
  suite again (step 5's detected command), commit, and push
  (`git commit -m "fix: address PR feedback — <short description>"`, then
  `git push origin <branch>`) once everything passes.

## 9. Write workspace context

```bash
mkdir -p .context
```

Create or overwrite `.context/pr.md`:

```markdown
# PR Context

- **PR**: #<number> — <title>
- **URL**: <url>
- **Branch**: `<branch>` → `<default branch>`
- **State**: <state>
- **Author**: <author.login>
- **Changes**: +<additions> / -<deletions> across <changedFiles> files
- **Absorbed**: backmerged with `<default branch>`, all tests passing
- **Outstanding discussion**: <none found | addressed now | left for the user, with a one-line list>

## Description

<body>
```

## 10. Report status

Print a short summary and stop — do not start implementing new features
beyond what the user confirmed in step 8:

```
Absorbed PR #<number>: <title>
Branch: <branch> (backmerged with <default branch>, pushed)
Tests: all passing
Discussion: <none outstanding | N item(s) addressed | N item(s) left open, listed above>
Context written to .context/pr.md
```

## Edge Cases

- **PR merged or closed**: warn the user, but proceed — they may want to
  review or extend the branch.
- **Unresolvable conflicts**: pause, show the conflict, and ask the user
  how to resolve it before continuing (step 4).
- **Tests failing beyond a simple fix**: describe what's failing and ask
  for guidance rather than guessing (step 6).
- **No unambiguous test command**: ask the user rather than skipping tests
  silently (step 5).
- **Branch already up to date with the default branch**: skip the merge
  and note it in the final summary.
