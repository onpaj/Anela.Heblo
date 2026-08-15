# `gh` CLI unavailable in some cloud/remote sessions

Some remote execution sessions (e.g. scheduled routines triggered via Claude
Code on the web) do not have a working `gh` CLI, even though CLAUDE.md says
"GitHub access via `gh` CLI only". Symptoms seen 2026-07-02:

- `gh auth status` reports the `GH_TOKEN` is invalid.
- Any `gh` subcommand that uses GitHub's GraphQL API (issue search with
  `--search`, `gh issue view`, etc.) fails with
  `HTTP 403: GraphQL proxying is not enabled.`

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

Confirmed again 2026-08-15 on a `/plan-next-task` scheduled run: this
session had `USE_GH_API=1` set (routing skill scripts through
`.claude/skills/_lib/gh_api.sh`'s curl+REST layer instead of the `gh`
CLI), and `claim_issue.sh`'s ref-creation call still failed — but with a
different, more specific symptom worth knowing: `gh_api.sh`'s shared
`req()` function calls `curl -d "$body"` without ever setting
`-H "Content-Type: application/json"`. curl defaults `-d` to
`application/x-www-form-urlencoded`, and GitHub's git-refs-creation
endpoint hard-rejects that with `403: Form-encoded request bodies are not
accepted on this endpoint. Send the documented JSON body.` — a real bug in
the shared library (not a rate limit, despite `req()`'s retry loop logging
it as "GitHub API 403 (rate limit?)"), and it likely affects every POST/
PATCH call in that library that sends a body, not just ref creation. Don't
spend time debugging/fixing it inline unless that's the actual task — it's
orthogonal to whatever GitHub issue you were sent to plan/implement.
Recognize the symptom (a POST/PATCH through `gh_api.sh` failing with this
exact "Form-encoded" message) and fall through to this note's established
workaround (MCP tools, direct implementation on the designated branch)
rather than retrying or treating it as transient.

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
