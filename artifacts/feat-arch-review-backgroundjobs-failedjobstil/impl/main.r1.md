All implementation commits are on the branch. Here is the output summary:

---

# Implementation: Decouple FailedJobsTile from Hangfire Infrastructure

## What was implemented

Introduced a thin `IFailedJobCounter` abstraction in the Application layer so `FailedJobsTile` no longer compiles against Hangfire's `JobStorage` type. A sealed `HangfireFailedJobCounter` adapter in `Anela.Heblo.API/Infrastructure/Hangfire/` implements the interface by wrapping `JobStorage.GetMonitoringApi().FailedCount()`. The pattern mirrors the existing `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler` convention. All tile metadata, JSON envelope shapes, log messages, and error strings are preserved byte-identical.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs` — new Application-layer interface with XML docs
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs` — sealed Hangfire adapter; no exception swallowing; CT comment per Amendment #4
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` — removed `using Hangfire;`, swapped `JobStorage` → `IFailedJobCounter`, `LoadDataAsync` is now `async`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — one new line: `services.AddScoped<IFailedJobCounter, HangfireFailedJobCounter>()` in the adapter block
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` — rewritten to mock `IFailedJobCounter`; propagation test renamed per Amendment #3
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs` — new adapter tests at `test/Features/BackgroundJobs/` per Amendment #2

## Tests

- `FailedJobsTileTests` (4 tests): ZeroFailures, PositiveCount, CounterThrows, TileMetadata — all green, no Hangfire imports
- `HangfireFailedJobCounterTests` (3 tests): happy path returns 42L, exception propagates, null ctor guard — all green
- Full BackgroundJobs test slice: 96 tests passed

## How to verify

```bash
# Build
dotnet build Anela.Heblo.sln

# Run new tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"

# Confirm no Hangfire using-directive in tile or interface
grep -rnE "^using Hangfire" \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/ \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs

# Confirm Application.csproj unchanged
git diff main -- backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

## Notes

- The 32 test failures in the full suite are pre-existing Docker/Testcontainers integration tests that require Docker running; unrelated to this change.
- `HangfireJobRegistrationHelper.cs` retains its `using Hangfire;` — that file is explicitly out of scope, tracked separately.
- The `sealed` modifier on `HangfireFailedJobCounter` is intentional per the spec and arch-review Decision #5; the code reviewer noted `HangfireJobEnqueuer` (older sibling) is not sealed — the inconsistency can be harmonized in a follow-up.

## PR Summary

Decouples `FailedJobsTile` from Hangfire's `JobStorage` infrastructure type by introducing an `IFailedJobCounter` abstraction owned by the Application layer. This closes the layering gap flagged by the arch-review routine on 2026-05-28 — the tile was the only remaining Application type that directly imported a Hangfire concrete class.

The change mirrors the `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler` pattern established by the earlier BackgroundJobs refactor: interface in `Application/Features/BackgroundJobs/Services/`, sealed adapter in `API/Infrastructure/Hangfire/`, DI registration in `AddHangfireServices`. Tile unit tests no longer reference any Hangfire assemblies; Hangfire mocking is now isolated to `HangfireFailedJobCounterTests`, which is the appropriate seam.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IFailedJobCounter.cs` — new interface
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireFailedJobCounter.cs` — new sealed adapter
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` — refactored to use abstraction
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — one new DI line
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` — rewritten mocks
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireFailedJobCounterTests.cs` — new adapter tests

## Status

DONE