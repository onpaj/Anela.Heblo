# SessionStart hook's `agentharness init` can silently regress repo-customized skill files

Confirmed 2026-08-22 on a `/plan-next-task` scheduled run.

The cloud environment's SessionStart hook upgrades the `agentharness` pip
package (e.g. 0.31.4 → 0.32.0) and then runs an "AgentHarness agents/skills
into the repo" copy step that overwrites files under `.claude/skills/`
with the generic upstream template for that package version — logged as
`wrote .claude/skills/...` lines in the hook output. This happens on every
container boot where a newer `agentharness` version is available, with no
review step, before any task-specific work starts.

This repo carries its own committed patches on top of some of those
generic templates. Two seen so far, both silently reverted by the sync
before I noticed:

- `.claude/skills/_lib/gh_api.sh`: this repo's committed version has
  `-H "Content-Type: application/json"` added to `req()`'s curl call (fix
  landed in PR #3944, see
  `memory/gotchas/gh-cli-unavailable-in-cloud-sessions.md`). The synced
  upstream template does not have this header — writing it back over the
  repo's file drops the fix, which would silently reintroduce the
  "Form-encoded request bodies are not accepted" 403 on every GitHub API
  write made through this library.
- `.claude/skills/applicationinsightsscan/telemetry-digest.sh` and
  `telemetry-rules.md`: this repo's committed version reports
  `HangfireJobFailed` events (PR #3929, "report Hangfire job failures to
  Application Insights"). The synced upstream template doesn't have this
  section — writing it back removes that reporting capability from the
  `applicationinsightsscan` skill.

The sync also drops in whole new files unconditionally
(`.claude/skills/_lib/flag_needs_work.sh`,
`.claude/skills/hygiene-pr/resolve_conflict.sh` were seen this way) that
implement a hygiene-pr conflict-resolution feature not yet present in this
repo's committed `SKILL.md` docs — i.e. upstream has shipped functionality
this repo hasn't adopted yet, dropped in as untracked files with no PR.

**What to do when a session finds unexpected working-tree modifications to
`.claude/skills/**` files it didn't touch itself, especially at the very
start of a session:** don't assume they're your own earlier WIP or another
session's leftover work. Check whether they match this pattern first —
`git diff <file>` on a file the sync log named, and see if the removed
side matches a documented fix/feature in `memory/gotchas/` or a merged PR.
If so, `git checkout -- <file>` to discard the regression (and `rm` any
newly-`wrote` untracked files) rather than committing it — committing it
would push the regression for real. This is environment/tooling behavior,
not something to "fix" as part of an unrelated task; if the upstream
feature (e.g. hygiene-pr conflict resolution) is actually wanted, that's
its own deliberate `update-agentharness` + review effort, not something to
wave through as a side effect of a scheduled routine's stop-hook demanding
a clean tree.
