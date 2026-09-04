# Pipeline `artifacts/feat-*` files are gitignored by a .NET `artifacts/` rule

`.gitignore` has a top-level `artifacts/` entry (line ~42, under the ".NET Core"
section — it's meant to exclude the `dotnet publish`/SDK artifacts output dir,
unrelated to the AgentHarness pipeline). This also matches the pipeline's own
`artifacts/feat-{issue_number}/` directory.

`git add -A` silently skips new files under `artifacts/feat-{issue_number}/`
(impl/, review/, task-context/, state.json on first creation, etc.) — the
commit succeeds but doesn't include them, and `git status` afterward shows a
clean tree, easy to miss. `git ls-files --error-unmatch` on the artifact path
(as the orchestrator templates require) then fails.

Fix: force-add pipeline artifact files explicitly, e.g.
`git add -f artifacts/feat-{issue_number}/impl/{task}.r{N}.md`, before
committing. Modifications to *already-tracked* artifact files (e.g. editing
`state.json` after it was force-added once) stage fine with plain `git add`
since git already tracks them — only newly created files under `artifacts/`
need the `-f`.

**Danger — do NOT use a blanket `git add -A -f`** (this is what the shipped
`.claude/agents/implement-orchestrator.md` template currently uses, per
issue #4036 / PR #4037). `-f` on a whole-repo `git add -A` force-adds
*every* gitignored path with pending changes on disk, not just
`artifacts/`. If a `dotnet build`/`dotnet test` ran in the worktree first
(e.g. as part of the developer or reviewer subagent's own verification
step, per `dotnet-build-hangs-nodereuse-accessmatrixgen.md`), every
`bin/`/`obj/` directory across the whole repo is sitting there with fresh
build output — `git add -A -f` will stage and commit thousands of those
binary/generated files in one shot (hit in practice: 4578 files,
~577k insertions, on feat-4042). Recovery is possible without a force-push
by `git rm -r --cached` the unwanted paths in a follow-up commit (leaves
them on disk, un-tracks them, no history rewrite) — but the safe fix is to
never take the blanket path to begin with: use `git add -A && git add -f
artifacts/feat-{issue_number}/` (untracked-but-gitignored artifacts only)
instead of `git add -A -f`, if editing the orchestrator template ever
becomes in scope.
