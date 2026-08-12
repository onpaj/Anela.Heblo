# Implementation: inject-timeprovider-into-catalog-merge-scheduler

## What was implemented

`CatalogMergeScheduler` now takes `TimeProvider` via constructor injection, matching every
sibling class in `Features/Catalog/Infrastructure/`. All clock reads (`DateTime.UtcNow`) were
replaced with `_timeProvider.GetUtcNow().UtcDateTime`, and the raw `new Timer(...)` debounce
timer construction was replaced with `_timeProvider.CreateTimer(...)` (backed by the `ITimer?`
field type). The dispose-then-recreate debounce reset shape (`_debounceTimer?.Dispose();`
immediately before re-creation) was left untouched, as was every other collaborator, log
message, and the `Stopwatch`/`Task.Run`/semaphore-timeout usages called out as out of scope.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs` — added `_timeProvider` field, re-typed `_debounceTimer` to `ITimer?`, added constructor parameter, replaced the two `DateTime.UtcNow` reads and the `new Timer(...)` call.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` — updated the single `CreateScheduler` construction helper (lines 31-40) to pass `TimeProvider.System`, so the 13 existing tests keep driving the real clock unchanged.

## Tests

- `CatalogMergeSchedulerTests` (13 `[Fact]`/`[Theory]` methods, not 12 as the task-context estimated — verified by grep count): all 13 pass unchanged, proving `TimeProvider.System` behaves identically to `DateTime.UtcNow`/`new Timer(...)` here.
- Wider `Features.Catalog` suite: 692 passed, 4 failed. All 4 failures are in `GetStockUpOperationsSummaryIntegrationTests`/other Testcontainers-backed Postgres integration tests, failing with `failed to connect to the docker API at unix:///var/run/docker.sock` — the sandbox has no Docker daemon running. Unrelated to this change (confirmed no Postgres/Testcontainers code path is touched by the diff).

## How to verify

```
cd backend
grep -n "DateTime\.UtcNow\|DateTime\.Now\|new Timer(" src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs
# expect: no output, exit 1
grep -rn "new CatalogMergeScheduler" .
# expect: exactly one hit, in CatalogMergeSchedulerTests.cs
dotnet build ../Anela.Heblo.sln
# expect: Build succeeded, 0 Error(s)
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CatalogMergeSchedulerTests"
# expect: Failed: 0, Passed: 13
dotnet format ../Anela.Heblo.sln --no-restore
git diff --stat
# expect: no changes outside the two files above
```

## Notes

- The task-context's expected test count (12) was off by one; the file actually has 13
  `[Fact]`/`[Theory]` methods. All 13 pass, so the behaviour-preservation gate is satisfied
  regardless of the exact count.
- `dotnet build` emits one pre-existing warning (MSB3073, access-matrix generator tool crash)
  unrelated to this change — present before this diff too.
- `CatalogModule.cs` and `ICatalogMergeScheduler.cs` were not touched, as required — DI resolves
  the new constructor parameter automatically since `TimeProvider.System` is already registered
  as a singleton in `ServiceCollectionExtensions.cs`.

## PR Summary

Injected `TimeProvider` into `CatalogMergeScheduler`, the last class in
`Features/Catalog/Infrastructure/` still reading the wall clock directly, matching the pattern
already used by `CatalogMergeService`, `CatalogCacheStore`, and `CatalogDataRefreshService`.
Both `DateTime.UtcNow` reads and the raw `new Timer(...)` debounce-timer construction now go
through the injected clock. No production behavior change — all 13 existing scheduler tests
pass unmodified except for a one-line update to the test helper's constructor call.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeScheduler.cs` — inject `TimeProvider`, replace `DateTime.UtcNow` and `new Timer(...)`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeSchedulerTests.cs` — pass `TimeProvider.System` in the `CreateScheduler` helper

## Status
DONE
