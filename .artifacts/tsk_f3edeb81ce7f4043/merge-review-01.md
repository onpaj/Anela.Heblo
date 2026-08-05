# Merge review — PR #3830

**Title:** [arch-review] BackgroundRefresh: BackgroundRefreshTaskRegistry re-registers every task unconditionally, discarding explicit RefreshTaskConfiguration
**Base:** main · **Head:** harness/tsk_29070694aebc4308 · **Closes:** #3829
**Diff:** 84 files, +4170 / −1

## Verdict: REJECT — scope violation

### The intended fix is good
The stated bug fix is exactly two files and is correct:

- `BackgroundRefreshTaskRegistry.cs`: the trailing unconditional
  `RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod)` is wrapped in an `else`, so an
  explicit `Configuration` is no longer overwritten by the app-settings-derived
  registration. This matches the bug described in #3829 and the plan/design artifacts.
- `BackgroundRefreshTaskRegistryTests.cs`: three new tests cover the three cases —
  explicit config honoured, null-config app-settings fallback unchanged, and no throw when
  the app-settings section is absent. Test construction follows the existing Moq pattern.

If this were the whole PR, it would be a clean, low-risk approve.

### Why it is rejected anyway
The branch diff against `main` contains far more than the fix. `merge-base(origin/main, HEAD)`
is `main`'s current tip, so **everything below actually lands on merge**, not just the fix:

1. **An entire unrelated feature — `docs/routines/test-health/`** (~3,000 lines):
   `test-health-digest.sh`, `rp-query.sh`, `gh-api.sh` and their `.test.sh` files, ~60
   ReportPortal/GitHub fixture JSONs, a harness installer (`harness/install.sh`,
   `test-health.agent.json`, `test-health.process.json`), plus a 1,594-line plan and a
   323-line design spec. None of this is mentioned in the PR body or issue #3829, and it is
   authored by a different task (the `feat(test-health)` commit series on this branch).

2. **Committed internal harness artifacts** — `.artifacts/tsk_29070694aebc4308/*.md`
   (plan/design/architecture/review notes) are checked into the repo tree. These are
   process scratch files, not source, and should not enter `main`.

The reviewer's first test is explicit: a change must do what its PR and issue say — no more,
no less; unrelated scope is a reason to withhold even when the code is good. This PR bundles
a 2-file bug fix with a whole separate routine and internal artifacts. The `test-health`
tooling (shell scripts, a harness installer that writes agent/process configs) has real
blast radius of its own and has not been reviewed against #3829's scope. It must be split
into its own PR.

A human needs to rebase/split this so the merge into `main` is only the BackgroundRefresh
fix (and to decide whether the `.artifacts/` files belong in the tree at all). That glance
is cheap; merging ~4k lines of unrelated, unreviewed tooling unattended is not.

```json
{"confidence": 0.06, "reasoning": "The stated fix is correct and well-tested, but the branch diff against main also carries an entire unrelated test-health routine (~3k lines) plus committed internal .artifacts files — a disqualifying scope violation for unattended merge.", "risks": ["Merges a large unrelated test-health feature (shell scripts, harness installer, fixtures, 1.9k lines of plans/specs) into main under a bug-fix PR", "Commits internal .artifacts/ planning files into the repo tree", "Unrelated test-health harness installer has its own unreviewed blast radius"]}
```
