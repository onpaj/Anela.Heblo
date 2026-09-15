# Subagent "wait for Monitor" ends its turn without actually waiting

When a background `Agent`-spawned subagent kicks off a long-running shell
command (e.g. `dotnet test Anela.Heblo.sln`) via `Bash` with
`run_in_background: true` and then tries to "wait" for it with the
`Monitor` tool, it can end its own turn immediately, reporting itself as
`completed` to the harness, while the background command is still running.
Nothing wakes it back up on its own — the parent session has to notice the
premature "I'll wait for the monitor to notify me" report and explicitly
`SendMessage` it to resume.

Seen twice in a row from the same subagent during a fanned-out
`implement-next-task`-style unit for feat-4191 (2026-09-15).

**Fix / workaround:** instruct subagents that need to wait on a long
command to run it in the **foreground** (no `run_in_background`) with a
generous timeout instead of backgrounding it and trying to `Monitor` it —
a foreground `Bash` call actually blocks until the process exits. Reserve
`Bash run_in_background` + `Monitor` for the top-level orchestrating
session, not for leaf subagents that have no way to be woken mid-task.
