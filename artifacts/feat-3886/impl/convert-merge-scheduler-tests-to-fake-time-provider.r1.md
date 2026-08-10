# Implementation: convert-merge-scheduler-tests-to-fake-time-provider

## What was implemented

Rewrote the entire `CatalogMergeSchedulerTests.cs` file to drive `CatalogMergeScheduler`'s
debounce and max-interval behaviour through `FakeTimeProvider` instead of real `Task.Delay`
waits. All 13 `[Fact]` methods were replaced with the exact code specified in the task-context
file, step by step (Steps 1-10):

- `CreateScheduler` now returns a 3-tuple `(sut, logger, time)`, constructing a
  `FakeTimeProvider` seeded at a fixed epoch (`TestStart = 2026-01-01T00:00:00Z`) and passing it
  into `CatalogMergeScheduler`'s constructor.
- Debounce/max-interval waits are driven via `time.Advance(...)` instead of sleeping.
- A deterministic two-step barrier (`WaitForMergeAsync` / `AwaitSignalAsync`, both built on
  `TaskCompletionSource` + `Task.WhenAny` with a failure-only `Task.Delay(SignalTimeout)`
  timeout) replaces polling loops and fixed-window `await Task.Delay(...)` assertions.
- Tests that only assert "no callback fires" (dispose/application-stopping cases) became
  synchronous (`void`, no `async`) since there is nothing to await under a fake clock.
- The one test that legitimately still pays real time —
  `ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`, which hits the production
  `_mergeSemaphore.WaitAsync(100)` contended path — is unchanged in that respect, per the
  task-context's explicit out-of-scope note.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` — full rewrite per the task-context's Steps 1-10; all 13 test names and the three log assertions (`Force executing merge`, `Merge already in progress, skipping`, `Background merge failed`/"boom") preserved.

`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs`
was **not** touched — that was task `inject-timeprovider-into-catalog-merge-scheduler`, already
completed in a prior revision.

## Tests

- `CatalogMergeSchedulerTests` (13 `[Fact]` methods) — covers debounce firing, burst collapsing,
  max-interval force path, concurrent-merge skip, `WaitForCurrentMergeAsync` (idle / blocking /
  after-dispose), dispose behaviour (before/after timer fires, double-dispose), application-stopping
  short-circuit, and callback-throws recovery with the error log assertion.

## How to verify

```bash
cd /home/user/worktrees/feature-3886-Arch-Review-Catalog-Catalogmergescheduler-Is-The-O

# grep gates
grep -n "Task.Delay" backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs
grep -n "Stopwatch\|DateTime.UtcNow" backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs

# build (solution is at the worktree root, not under backend/)
dotnet build Anela.Heblo.sln

# scheduler tests only
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMergeSchedulerTests" --no-build -v n
# expect: Failed: 0, Passed: 13

# determinism (5x)
for i in 1 2 3 4 5; do
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMergeSchedulerTests" --no-build || echo "RUN $i FAILED"
done

# wider Catalog suite
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Catalog" --no-build

# format
cd ..
dotnet format Anela.Heblo.sln --no-restore
git diff --stat
```

## Notes

- **Solution/build paths differ from the task-context's literal commands.** `Anela.Heblo.sln`
  lives at the worktree root, not under `backend/`. I ran `dotnet build Anela.Heblo.sln` and
  `dotnet format Anela.Heblo.sln --no-restore` from the worktree root instead of `backend/`.
  Build succeeded (0 errors, 92 pre-existing warnings unrelated to this change).
- **Added `--no-build` to the Step 13 `dotnet test` invocation.** Without it, `dotnet test`
  re-evaluates and rebuilds the full solution dependency graph (already built cleanly in the
  prior `dotnet build` step) and along the way re-runs a `GenerateAccessMatrix` MSBuild target
  that shells out to a separate `dotnet run --project .../Anela.Heblo.AccessMatrixGen` tool. That
  tool currently throws an unhandled `JsonException` in this sandbox (pre-existing, unrelated to
  this change — the prior task's impl notes for `inject-timeprovider-into-catalog-merge-scheduler`
  already flagged an "access-matrix generator tool crash" warning from the same code path). The
  rebuild-plus-tool-crash path was extremely slow here (~13 minutes) before I killed it and reran
  with `--no-build`, which is exactly what the task-context's own Step 14 already does for the
  determinism loop. With `--no-build`, the scheduler test class ran in ~200-230 ms per run.
- **The Step 11 `grep -n "Stopwatch\|DateTime.UtcNow"` check has one expected hit**, not zero: the
  string literal `"GetLastMergeTime must keep the Kind that DateTime.UtcNow produced"` inside the
  FluentAssertions `.Because(...)` message on the first test — this is verbatim text the
  task-context itself specifies in Step 2's code block. It is a message string, not an actual
  `DateTime.UtcNow` call site, so it does not violate the intent of the check (no wall-clock reads
  remain in the test file). The `Task.Delay` grep matched exactly 3 code occurrences plus 1
  explanatory comment line, as expected.
- **Wider `Features.Catalog` suite:** 692 passed, 4 failed — all 4 are
  `GetStockUpOperationsSummaryIntegrationTests` (Testcontainers-backed Postgres integration
  tests) failing with `Docker is either not running or misconfigured` because this sandbox has no
  Docker daemon. Same pre-existing, unrelated failure documented in the prior task's impl notes.
- `dotnet format` made no changes; `git diff --stat` after formatting shows only
  `CatalogMergeSchedulerTests.cs` (plus the orchestrator-managed `artifacts/feat-3886/state.json`
  checkpoint file) touched.

## PR Summary

Rewrote `CatalogMergeSchedulerTests.cs` to drive every debounce/max-interval scenario through
`FakeTimeProvider.Advance(...)` instead of real `Task.Delay` sleeps, closing out FR-5/NFR-1 from
the parent spec. A `TaskCompletionSource`-based two-step barrier (signal from inside the merge
callback, then await `WaitForCurrentMergeAsync()`) replaces every polling loop and fixed-window
sleep used to observe merge completion or "nothing else fired." All 13 existing test names and
the three behaviour-guarding log assertions survive unchanged. The only remaining real-time cost
is the intentionally out-of-scope `_mergeSemaphore.WaitAsync(100)` contended-path wait inside
`ExecuteMergeAsync_WhenMergeAlreadyInProgress_SkipsSecondInvocation`. Five consecutive runs of the
class confirm determinism (no flakes), each completing in ~200-230 ms versus the prior file's
multi-second real-time waits.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` — full rewrite to use `FakeTimeProvider`, `TaskCompletionSource` barriers, and exact-instant/`DateTimeKind` assertions instead of real-time sleeps and `Stopwatch` timing checks.

## Status
DONE
