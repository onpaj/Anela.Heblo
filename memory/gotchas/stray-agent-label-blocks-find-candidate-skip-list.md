# Stray `agent` label makes `find_candidate.sh` skip already-progressed issues every cycle

Observed 2026-08-13 during a scheduled `/plan-next-task` run: `find_candidate.sh`
returned `candidate: null` with 10 issues in `skipped`, all reason
"already has a feature/{n}-* branch (claim likely did not fully complete)"
(issues #3877, #3881, #3883, #3884, #3889, #3891, #3892, #3893, #3894, #3895 —
all `[arch-review]`-filed issues).

Checked each via `.claude/skills/_lib/gh_api.sh issue-view <n>` (not
`gh issue view --json`, which 403s under `USE_GH_API=1` sessions — GraphQL is
blocked, see `gh-cli-unavailable-in-cloud-sessions.md`): every one of them
still carries the `agent` label **alongside** a later-stage label
(`agent-completed` or `agent-ready-for-dev`, some also `groomed`). They are
not actually stuck claims — they already finished planning (or the whole
pipeline) — some earlier label-swap step just never removed the original
`agent` label when it added the later-stage one.

Effect: `find_candidate.sh`'s fresh-candidate walk treats every `agent`-labeled
issue as a planning candidate, finds an existing `feature/{n}-*` branch, and
reports it "skipped" with a misleading "claim likely did not fully complete"
reason — every single cycle, forever, for these issues, cluttering the skip
list. It does NOT block progress on genuinely fresh issues (the walk continues
past skipped ones), so a `candidate: null` result together with a skip list
made up entirely of issues that also carry a later-stage label just means
"nothing new to plan right now," not a real backlog jam. Don't treat it as
urgent; don't try to "fix" it from inside `/plan-next-task` (out of scope —
removing a stray label from an already-completed issue is a hygiene task for
whatever skill manages label swaps, not the planning worker).
