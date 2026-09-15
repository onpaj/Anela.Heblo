# Agent `context_files` pointing at the superpowers plugin cache resolve to nothing

`.agents/planner.md`, `.agents/brainstorm.md`, and `.agents/developer.md` (installed by
`agentharness init`) each declare a `context_files` entry like:

```yaml
context_files:
  - ~/.claude/plugins/cache/*/superpowers/*/skills/writing-plans/SKILL.md
```

This assumes the third-party `superpowers` plugin marketplace is installed. In this
project's cloud execution environment, only the `claude-plugins-official` marketplace
is registered (`~/.claude/plugins/known_marketplaces.json`) — `superpowers` isn't, so
`~/.claude/plugins/cache/` doesn't even exist and the glob matches zero files.

`plan-orchestrator`'s own instructions treat an empty `context_files` match as a hard
stop by design ("an agent whose declared skill silently resolved to nothing runs
without the instructions it was written around, and nothing downstream can tell that
it did") — correct behavior, but it means the *planner* phase (always last in
analyst → architect → designer → planner) dies every single time in this environment,
after already spending 3 LLM calls on the earlier phases.

**Fix applied (2026-09-13, PR #4162):** repoint the three `context_files` entries at
the equivalent skill files already checked into this repo under `.claude/skills/`
(`writing-plans`, `brainstorming`, `subagent-driven-development` — same content,
different install path). Verify content parity before trusting a substitution like
this; don't just point at "a skill with the same name" blindly.

**Risk of recurrence:** `agentharness init --force` (run by the SessionStart hook)
regenerates `.agents/*.md` from the upstream `harness` package template on every
session start, the same way it has repeatedly reverted the `gh_api.sh` Content-Type
fix (see `memory/gotchas/gh-cli-unavailable-in-cloud-sessions.md`). If this regresses
again, the permanent fix belongs upstream in `onpaj/harness`, not just in this repo's
checked-in copy.

**Recurred (2026-09-15, issue #4177, PR #4194):** exactly as predicted — commit
`d7584a3` ("chore: update skill paths and fix gh_api check-run deduplication",
applied by session-start-hook) reverted `.agents/planner.md`'s `context_files` back
to the broken plugin-cache glob, on top of an earlier revert at `340f5b1`. This is
now at least the third revert/refix cycle for this exact line. This time, rather than
re-landing another same-shaped repo-local PR (which the hook will just revert again
next session), the fix was applied *only* to the local worktree's uncommitted working
copy — enough to unblock the `planner` phase for this one planning run — and
deliberately **not** committed anywhere. The permanent fix still belongs upstream in
`onpaj/harness`'s `agentharness init` template; until that lands, expect this to keep
costing one wasted `analyst`→`architect`→`designer` cycle's worth of LLM calls (the
planner always fails last) every time a session starts fresh after a hook run.
