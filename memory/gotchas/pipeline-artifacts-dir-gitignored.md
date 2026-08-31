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

**Regression, 2026-08-31**: this exact fix (PR #3991, commit `45fb0ef`,
which added `-f` to `git add -A` in `.claude/agents/orchestrator.md`,
`.claude/agents/plan-orchestrator.md`, and `.claude/skills/oneshot/SKILL.md`)
was reverted by a later commit `72784c2` — "chore: update orchestrator and
oneshot skill templates (remove -f from git add)", whose own message says
**"Applied by session-start hook"**. `.gitignore` still excludes `artifacts/`
at the time of writing, so this reintroduced the exact silent-artifact-loss
bug this file documents. This is a *different flip-flop mechanism* than the
`gh_api.sh` Content-Type header one (that one was repeated manual edits
across sessions) — here a **session-start hook itself** is the thing
undoing the fix, which means simply re-fixing the templates again is not
durable; whatever hook logic is auto-applying this "remove -f" change needs
to be found and stopped, or every future planning/oneshot run will hit this
same silent data loss again. Worked around for issue #4000's `/plan-next-task`
run by force-adding (`git add -A -f artifacts/feat-4000`) directly in the
session rather than editing the templates. Flagged to the user rather than
"fixed" in-place, since editing the templates again would just be reverted
by the same hook at the next session start.
