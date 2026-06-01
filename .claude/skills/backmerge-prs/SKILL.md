---
name: backmerge-prs
description: Merge the base/target branch back into each feature branch (backmerge). Use when the user says "backmerge PRs", "backmerge PR 717", "sync open PRs with base", or "merge base into feature branches". Without a PR number, walks all open PRs. With a PR number, targets that single PR. Skips merged/closed PRs, handles conflicts gracefully, prints a summary table, and restores the original branch when done.
---

# Backmerge PRs

Merges each PR's base branch into its head branch (backmerge).

## Usage

```
/backmerge-prs                      # all open PRs, each PR's base branch
/backmerge-prs 717                  # single PR, its base branch
/backmerge-prs --from main          # all open PRs, merge main into each head
/backmerge-prs 717 --from main      # single PR, merge main into head
```

## Steps

1. **Run the script** from the repo root:
   ```bash
   bash .claude/skills/backmerge-prs/scripts/backmerge_prs.sh [<pr_number>] [--from <branch>]
   ```
2. **Review summary table** — each PR shows `done`, `done (auto: N add/add)`, `skipped`, or `conflict: <files>`
3. **For remaining conflict PRs** — resolve manually:
   ```bash
   git checkout <head-branch>
   git merge origin/<branch>
   # resolve conflicts
   git add . && git commit && git push origin <head-branch>
   ```

## What the script does

- `git fetch --all --prune` at start
- No args → fetches all `--state open` PRs via `gh pr list`
- One arg → fetches that specific PR via `gh pr view`
- `--from <branch>` → overrides the merge source for every PR (default: each PR's own base branch)
- Skips MERGED and CLOSED PRs automatically
- Skips PRs where head branch equals the merge source
- Checks out head branch, merges `origin/<source>` with `--no-edit`, pushes on success
- On merge failure, attempts **auto-resolution** (see below) before giving up
- Restores original branch at the end via `trap`

## Auto-resolution logic

After a failed merge, the script categorises conflicts:

| Conflict type | Git status code | Action |
|---------------|-----------------|--------|
| Both branches added the same file | `AA` | `git checkout --theirs` (take base version), stage, commit, push |
| Both branches modified the same file | `UU` | Abort merge, report for manual resolution |

**Rule:** if the merge has only `AA` conflicts and zero `UU` conflicts, the script fully resolves and pushes automatically. Any `UU` conflict aborts the whole merge and leaves it for manual work.

## Manual conflict resolution notes (Anela.Heblo)

Common `UU` conflicts in this repo and how to resolve them:

- **Program.cs / ApplicationModule.cs / ApplicationDbContext.cs** — keep both registrations (base added a new module/entity, head added another); manually merge both additions
- **Migration files** (`Migrations/`) — each branch likely adds its own migration; keep both files, rename if timestamps clash
- **OpenAPI client** (`frontend/src/api/`) — regenerated on build; take `--theirs` (base version) and let CI regenerate on merge

After resolution:
```bash
git add <files>
git commit
git push origin <head-branch>
```
