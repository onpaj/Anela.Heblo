# `gh` CLI unavailable in some cloud/remote sessions

Some remote execution sessions (e.g. scheduled routines triggered via Claude
Code on the web) do not have a working `gh` CLI, even though CLAUDE.md says
"GitHub access via `gh` CLI only". Symptoms seen 2026-07-02:

- `gh auth status` reports the `GH_TOKEN` is invalid.
- Any `gh` subcommand that uses GitHub's GraphQL API (issue search with
  `--search`, `gh issue view`, etc.) fails with
  `HTTP 403: GraphQL proxying is not enabled.`

Seen again 2026-08-07 with a different 403 body — `GitHub access is not
enabled for this session. An org admin must connect the Claude GitHub App
for this organization.` — and covering *all* REST endpoints for this repo,
not just GraphQL: `gh api user` still succeeds, but
`gh api repos/onpaj/Anela.Heblo/issues` 403s. Don't conclude `gh` works
just because `gh api user` does. Note that git push/fetch keep working
throughout; they go through a separate proxy from the GitHub API. Routing
around the 403 (a `gh` wrapper that unsets `HTTPS_PROXY` and substitutes a
PAT) is not an option — the session's proxy README explicitly forbids it.

These sessions instead expose the GitHub MCP server tools
(`mcp__github__*`) and explicitly instruct using those for all GitHub
interactions. When `gh` fails this way, don't retry — switch to the MCP
tools directly (`list_issues`, `issue_write` for label edits — labels must
be passed as the full desired set since `issue_write` isn't additive,
`create_pull_request`, `pull_request_read`). PRs share the issue label
endpoint, so `issue_write` with the PR number also works to label a PR.

Also: these sessions frequently pin a single designated branch
(`Develop on branch <name>`, `NEVER push to a different branch without
explicit permission`) that overrides the `oneshot`/`chopchop` skills'
default `feature/{issue}-{slug}` branch-per-issue convention. When that
happens, skip creating a new worktree/branch and commit directly to the
designated branch instead — the skills' branch naming is a default, not a
hard requirement, and the environment's explicit branch pin takes
precedence.

When the designated-branch override applies, also skip the full
AgentHarness multi-agent `orchestrator`/`agentharness checkpoint` pipeline
(analyst → architect → designer → planner → developer → reviewer with a
committed `artifacts/feat-{id}/` tree) — it assumes its own worktree and
branch. Two more reasons it doesn't fit this repo as-is: `artifacts/` is
gitignored at the repo root (`.gitignore:42`, added for .NET build output,
collides with the harness's artifact-folder name), so committing that tree
requires `git add -f`; and `agentharness checkpoint`/orchestrator steps call
`gh issue view`/`gh pr create` internally, which fail the same way plain
`gh` does. For a small, well-scoped fix (e.g. a duplication/coverage-gap
arch-review finding), it's simpler to just read the issue body from the
`list_issues` MCP result, implement directly on the designated branch, run
the project's real build/tests, and open the PR with
`mcp__github__create_pull_request` + `issue_write` for the label — same
outcome (labeled PR, `Closes #N`, `#N: <summary>` title) without the
pipeline machinery.

That said, the pipeline *can* be run under these conditions when the task
is `/plan-next-issue` (or another skill whose whole point is the pipeline)
— done successfully for #3887 on 2026-08-07. What it takes:

- Replicate `find_candidate.sh` by hand: `mcp__github__list_issues` for the
  `agent`-labeled pool, then `git ls-remote --heads origin 'feature/{n}-*'`
  to skip already-claimed issues. Both halves work; only the `gh` glue
  doesn't.
- Replace `claim_issue.sh`'s refs-API test-and-set with
  `git push origin <base-sha>:refs/heads/<branch>` — same atomicity, and
  git push is unaffected by the API 403.
- Write `artifacts/feat-{n}/brief.md` yourself from the `issue_read` body
  before invoking the orchestrator; that's the orchestrator's only `gh`
  dependency (its Setup step 2).
- Act as `plan-orchestrator` yourself and spawn analyst/architect/designer/
  planner as subagents directly. Spawning it as a subagent doesn't work —
  it needs to spawn subagents of its own, and nested subagents aren't
  allowed.
- `agentharness checkpoint init/phase/status/tasks` never touches GitHub —
  it works fine.
- Every commit needs `git add -f artifacts/feat-{n}` (the gitignore
  collision above); the orchestrator doc's plain `git add -A` silently
  fails on a *new* artifact dir. Existing artifact trees on `main` are
  already tracked, so they don't show this.
