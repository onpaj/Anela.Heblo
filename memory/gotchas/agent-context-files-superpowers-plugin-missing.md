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

**Confirmed recurring, 2026-09-17** on a `/plan-next-task` run for issue #4210: found
`.agents/planner.md`'s `context_files` back on the broken
`~/.claude/plugins/cache/*/...` glob on `main`, and `.agents/developer.md` and
`.agents/brainstorm.md` broken the same way. `git log` on each file shows the pattern
predicted by `memory/gotchas/gh-cli-unavailable-in-cloud-sessions.md`'s "Root cause of
the repeat-revert loop" section: innocuous-looking commits like `d7584a34` ("chore:
update skill paths...") and `f7a3e77d` ("chore: update subagent-driven-development
skill path in developer agent config") are a later session accidentally *committing*
the SessionStart hook's uncommitted `agentharness init --force` revert instead of
discarding it with `git checkout --`/`git restore` as that note instructs. Re-applied
the fix to all three files plus this note (PR from branch
`fix/agent-context-files-superpowers-plugin-regression`) — but per the note above,
expect this to regress again the next time a session commits the hook's dirty diff
without checking `git status`/`git diff` first. If you're that session: a dirty
`.agents/`/`.claude/` tree right after a fresh checkout, showing exactly these
plugin-cache paths reappearing, is the hook, not real work — discard it, don't commit
it.
