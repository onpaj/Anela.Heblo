# agentharness init clobbers repo-local .claude/skills/ patches

Every cloud session's SessionStart hook re-runs the AgentHarness scaffolding
copy (writes `.agents/`, `.pipeline/config.json`, `.claude/agents/`,
`.claude/skills/**` from the installed `agentharness` package) before the
session's actual task starts. This is a blind overwrite, not a merge: any
repo-specific edit previously made directly to a file under `.claude/skills/`
gets silently reverted back to the upstream package's version, with no
warning and no diff shown to the task at hand.

Confirmed instances lost by this on 2026-08-19 (session on branch
`claude/upbeat-volta-ucqx4i`, working on an unrelated `/implement-next-task`
run that found nothing to implement):

- `.claude/skills/_lib/gh_api.sh` — reverted the `Content-Type:
  application/json` header on POST/PATCH bodies added in PR #3944 (a real
  bug fix for the GitHub REST API).
- `.claude/skills/applicationinsightsscan/telemetry-digest.sh` and
  `telemetry-rules.md` — reverted the Hangfire job-failure telemetry
  rule/query added in PR #3929.

Both were manually restored and re-committed in this session (commit
`b1c1974` on `claude/upbeat-volta-ucqx4i`) alongside that session's
legitimate framework-sync changes.

**Implication:** any future manual edit to a file under `.claude/skills/`,
`.claude/agents/`, `.agents/`, or `.pipeline/config.json` is at risk of
being silently dropped the next time *any* cloud session boots — not just
sessions that intentionally run `/update-agentharness`. If a session
notices unexpected diffs to these paths at start (dirty tree from the
SessionStart hook, not from its own work), diff each changed file against
its last commit before blindly committing the sync — a hunk that *removes*
functionality may be reverting a deliberate repo-specific fix, not just
picking up an upstream improvement.

**Root cause not fixed here:** the SessionStart hook/setup script doing the
scaffolding copy has no mechanism to preserve local patches (e.g. a merge,
or skipping files with local modifications). That would need to be fixed in
the environment's setup script itself, which is outside a normal
`/implement-next-task` or `/plan-next-task` run's scope.
