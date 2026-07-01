# gh CLI unauthenticated in cloud sessions — use GitHub MCP tools instead

In Claude Code web/cloud sessions for this repo, `gh auth status` fails —
both the ambient `GH_TOKEN` and the `.env` `GITHUB_TOKEN` are invalid for the
`gh` CLI, even though the GitHub MCP server (`mcp__github__*` tools) works
fine with its own auth. This breaks any skill step written as a `gh ...`
command (`gh issue list`, `gh issue edit`, `gh pr create`, `gh pr edit`, the
`agentharness convert` command, `ensure_pr_linked.sh`).

Workaround used successfully (2026-07-01, feat-3445): substitute each `gh`
call with the MCP equivalent:
- `gh issue list --label agent` → `mcp__github__list_issues` (owner/repo,
  labels, orderBy: CREATED_AT, direction: ASC)
- `gh issue edit --add-label/--remove-label` → `mcp__github__issue_write`
  method=update with the **full desired label list** (it replaces, not
  patches)
- `gh pr list` → `mcp__github__search_pull_requests` with
  `query: "repo:OWNER/REPO is:pr head:BRANCH_PREFIX"` (works well; `list_pull_requests`
  has no head-branch filter and can blow the token budget on a big repo)
- `gh pr create` → `mcp__github__create_pull_request`, then
  `mcp__github__issue_write` (issue_number = PR number) to add labels since
  `create_pull_request`/`update_pull_request` have no labels param
- `gh pr view --json body` verification → `mcp__github__pull_request_read`
  method=get

Also: the installed `agentharness` CLI (v0.13.0) has **no `convert`
subcommand** despite the `convertforagent` skill referencing
`agentharness convert <issue-number>`. `agentharness checkpoint init
<issue-number>` alone is sufficient to bootstrap `feat-<N>` state — it does
not need a prior `convert` step; run it directly inside the feature worktree
(not the main checkout).

Also: the repo default branch is `main`, not `master`, even though some
skill docs/examples say `--base master`.
