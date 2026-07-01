# Running oneshot/chopchop in a Claude Code web/cloud session

The `oneshot` and `chopchop` skills are written assuming the `gh` CLI works. In
a Claude Code web/cloud session, `gh` and direct GitHub REST calls are **not**
available ("GitHub access is not enabled for this session" / "GraphQL
proxying is not enabled") — use the `mcp__github__*` tools for every step
instead (list_issues, issue_read, issue_write for labels, create_pull_request,
issue_write again on the PR number for the `agent` label since PRs share the
issue-number namespace).

Other gotchas hit while adapting the pipeline:

- **`artifacts/` is gitignored** at the repo root. Every `git add -A
  artifacts/feat-{id}` step in `orchestrator.md` needs `-f` (`git add -f -A
  artifacts/feat-{id}`) or the commit silently no-ops ("nothing to commit").
- **Default branch is `main`, not `master`.** The oneshot skill template says
  `--base master` — use `main`.
- **Bash tool cwd resets to the original directory after every call** in this
  environment (unlike a persistent shell) — `cd "$WT" && ...` must be repeated
  in full inside every single Bash invocation; a `cd` in one call does not
  carry over to the next.
- **New worktrees are missing `.agents/code-reviewer.md`.** In this repo's
  session, `.agents/code-reviewer.md` only exists on the long-lived designated
  session branch (`claude/peaceful-carson-bqk251`), not on `origin/main` — a
  fresh worktree branched from `origin/main` won't have it. Read it from the
  main checkout (`/home/user/Anela.Heblo/.agents/code-reviewer.md`) instead.
- **`agentharness checkpoint init {issue}` needs a brief written manually**
  first (`artifacts/feat-{issue}/brief.md`), since `gh issue view --json body`
  isn't available — fetch the body via `mcp__github__issue_read` and write it
  yourself.
