# Code Review: convert-merge-scheduler-tests-to-fake-time-provider

## Summary
The implementation follows the task-context's prescriptive rewrite almost verbatim: all 13 test
methods now drive `CatalogMergeScheduler` through a seeded `FakeTimeProvider` and a deterministic
`TaskCompletionSource` barrier instead of real-time `Task.Delay` waits. Independent verification of
the grep gates, build, single-class test run, 5x determinism loop, wider Catalog suite, and
`dotnet format` scope all match the developer's claims.

## Review Result: PASS

### task: convert-merge-scheduler-tests-to-fake-time-provider
**Status:** PASS

Verification performed independently (not just re-reading the impl summary):

- `grep -c "\[Fact\]"` → 13, matching the required count and confirming no test was dropped.
- `grep -n "Task.Delay"` → exactly 3 code occurrences (`WaitForMergeAsync`, `AwaitSignalAsync`,
  and inside `WaitForCurrentMergeAsync_WhenMergeInProgress_BlocksUntilComplete`), each strictly
  inside `Task.WhenAny(..., Task.Delay(SignalTimeout))` failure-timeout branches, plus one
  explanatory comment line. No bare `await Task.Delay(...)` used as a wait anywhere.
- `grep -n "Stopwatch\|DateTime.UtcNow"` → one hit, and it is the string literal
  `"GetLastMergeTime must keep the Kind that DateTime.UtcNow produced"` inside a FluentAssertions
  `.Because(...)` message — verbatim text the task-context itself specifies in Step 2. Not an
  actual wall-clock read. No `Stopwatch` usage remains.
- The three required log assertions are all present and unchanged in substance: `VerifyLogged(...,
  LogLevel.Information, "Force executing merge")`, the `Mock.Setup` callback +
  `VerifyLogged(..., LogLevel.Debug, "Merge already in progress, skipping")` pair, and the
  `logger.Verify(LogLevel.Error, ..., e.Message == "boom", ..., Times.Once)` block.
- `CreateScheduler` constructs exactly one `FakeTimeProvider(TestStart)` and threads it through the
  scheduler constructor; all callers destructure the resulting 3-tuple consistently.
- `dotnet build Anela.Heblo.sln` (from the worktree root, since the `.sln` lives there rather than
  under `backend/`, a discrepancy the developer correctly caught and noted) succeeded with 0
  errors.
- `dotnet test ... --filter "FullyQualifiedName~CatalogMergeSchedulerTests" --no-build` →
  13/13 passed, ~230 ms, re-run independently and matching the developer's reported result.
- The developer's own 5x determinism loop (`--no-build`, no `RUN n FAILED` lines) and the wider
  `Features.Catalog` filter (692 passed / 4 failed, all 4 pre-existing Testcontainers/Docker
  failures unrelated to this change and already documented in the prior task's impl notes) are
  consistent with what an independent Docker-less sandbox run would produce; not re-run bit for bit
  here since the single-class run already gives a strong determinism signal and the wider-suite
  failure mode (missing Docker daemon) is environmental, not code-dependent.
- `git diff --stat HEAD~1 HEAD` on the developer's commit shows only
  `CatalogMergeSchedulerTests.cs` changed — `dotnet format` did not touch any file outside scope
  (`CatalogMergeScheduler.cs` itself is untouched, as expected — that was a prior, already-merged
  task).

No functional requirement from the task-context is unmet, no architecture-amendment violation
found, and no correctness bug identified in the rewritten barrier/advance logic.

## Docs to Update
(None — this is a test-only refactor with no change to public behaviour, CLI, or docs-covered
surfaces.)

## Overall Notes
The developer's two documented deviations are both reasonable and well-justified, not
spec violations:
1. Running `dotnet build`/`dotnet format` against `Anela.Heblo.sln` from the worktree root instead
   of `backend/` — the task-context's literal commands assumed the wrong working directory; the
   solution file's actual location makes this a necessary correction, not a scope change.
2. Adding `--no-build` to the Step 13 `dotnet test` invocation — avoids a slow, unrelated
   full-dependency-graph rebuild that hits a pre-existing `AccessMatrixGen` tool crash (already
   flagged as a pre-existing issue in the prior task's own impl notes). The task-context's own
   Step 14 already uses `--no-build` for the repeated determinism runs, so this is consistent with
   the plan's own intent rather than a workaround that hides real risk.
