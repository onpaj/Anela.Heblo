# Parallel `lease.sh acquire` calls from sibling worktrees can all spuriously fail

When fanning out implement-stage work across several GitHub issues at once by
spawning multiple agents with `isolation: worktree` (or any setup where
several worktrees of the *same local checkout* run concurrently), do **not**
have each worktree call `.claude/skills/_lib/lease.sh acquire feat-N`
independently and in parallel — even though each acquires a *different*
lease id (a different `refs/heads/agent-leases/feat-N` ref).

Observed: fanning out 4 agents in parallel (issues #4171/#4173/#4174/#4175),
each doing `lease.sh acquire` as its first step, all 4 failed simultaneously
with exit 3 / "lost the race to acquire" — even though the leases were, in
fact, uncontended (`lease.sh status` immediately after showed the exact same
`expired`/`absent` state as before any of them ran, i.e. nobody's push had
actually landed as a live held lease). Sibling worktrees share the same
local `.git` object/ref storage, and concurrent `git fetch`/`git push`
touching refs under the same repo can collide at the local git level
(packed-refs / lock files) regardless of the fact that the ref *names*
differ. `push_lease` treats any push failure identically to "someone else
moved the ref" (see `_lib/lease.sh`'s `cmd_acquire`), so this local
self-collision is indistinguishable from a real competing worker in the
reported result.

**Fix that worked:** acquire all needed leases *sequentially* from the
primary checkout (fast — plain git ref operations, no build/test involved)
*before* dispatching the parallel worktree agents. Tell each dispatched
agent the lease is already held (skip its own `acquire` call) and have it
only call `lease.sh release feat-N` once, at the very end. `acquire`
records the holder identity under the git *common* dir
(`<common>/agentharness-leases/<lease-id>`), which is shared across every
worktree of the same checkout, so a later `release` from within a sibling
worktree (without `AGENT_LEASE_HOLDER` set) correctly reads back the
identity the primary checkout's `acquire` recorded and releases the right
lease.

Even with pre-acquired leases, the end-of-run `release` calls from several
worktrees could in principle still collide the same way — but `release`
failing is not fatal (the lease just sits until its TTL expires), unlike
`acquire` failing, which throws away the whole unit of work. Have redispatched
agents retry a failing `lease.sh` call once after a short pause rather than
treating a single non-zero exit as authoritative.
