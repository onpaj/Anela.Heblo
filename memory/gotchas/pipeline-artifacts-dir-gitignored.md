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
