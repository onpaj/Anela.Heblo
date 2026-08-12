# plan-next-task: orphaned `agent`-labeled branches never self-heal

`find_candidate.sh` skips any `agent`-labeled issue that already has a
`feature/{n}-*` branch on origin (assumes claim succeeded and label swap to
`agent-planning` just lagged). Stale-reclaim only re-checks issues labeled
`agent-planning` — so if `claim_issue.sh` creates the branch but its label
swap fails *before* ever setting `agent-planning` (issue stays on `agent`),
that issue is permanently skipped by every future cycle. It can never
self-heal through the normal `find_candidate.sh` path.

Observed 2026-08-12: issues #3881, #3883, #3884 (all `[arch-review]`,
`agent` + `arch-review` labels only) each have a `feature/{n}-*` branch on
origin but no PR and no further pipeline-stage label. Confirmed via
`.claude/skills/_lib/gh_api.sh pr-view "$BRANCH"` returning empty for all
three.

Fix/workaround: run `/plan-next-task <issue-number>` explicitly for each
stuck issue — the skill's "Targeting a specific issue" mode treats a
branch-exists-no-PR issue as a `stale-reclaim` self-heal case regardless of
its current label.
