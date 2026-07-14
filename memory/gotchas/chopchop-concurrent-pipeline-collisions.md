# chopchop can collide with a concurrently-running pipeline

Seen 2026-07-14: a `/chopchop` run picked issue #3633 (oldest open `agent`
issue with no PR at the time of the check), ran the full oneshot pipeline
(analyst → architect → planner → developer → reviewer → code-reviewer) to
completion, and only discovered when pushing that another automated run had
already opened PR #3639 for the *same* issue on the *same* deterministic
branch name (`feature/{issue}-{slug}` is derived identically every time, so
two independent runs on the same issue always collide on the branch name).

In this case all 5 open `agent` issues from that day's arch-review batch
(#3633–#3637) turned out to already have PRs by the time the check was
redone — a second/parallel pipeline instance had worked through the entire
queue faster.

**Lesson:** the initial "does a PR exist for `feature/{N}-` " check in
`chopchop` step 2 is a race — re-verify immediately before pushing, not just
at pipeline start, since the pipeline itself can take many minutes (this run
took ~40 min end-to-end) during which another instance can finish the same
issue. If a duplicate PR turns up at push time:

- Do NOT force-push over the existing branch/PR.
- Delete the local worktree/branch (`git worktree remove --force ...`,
  `git branch -D ...`) — safe, it only existed locally.
- Fix the issue's labels back to whatever the existing PR implies (usually
  `agent-completed` if a PR already references `Closes #N`) — the duplicate
  run's own `agent → agent-wip` label flip may have clobbered a correct
  `agent-completed` label that the other pipeline had already set.
- If every candidate issue in the walk turns out to already have a PR,
  that's the normal "nothing left to start" exit — no need to treat it as
  an error, just don't fabricate new work.
