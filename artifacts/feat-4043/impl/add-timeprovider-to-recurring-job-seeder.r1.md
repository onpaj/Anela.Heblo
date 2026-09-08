# Implementation: add-timeprovider-to-recurring-job-seeder

## What was implemented
Replaced both `DateTime.UtcNow` call sites in `RecurringJobSeeder.SeedDefaultConfigurationsAsync` with a single `_timeProvider.GetUtcNow().UtcDateTime` value computed once per call, injected via a new `TimeProvider` constructor parameter. This makes the seeder's timestamp deterministic and testable, matching the existing `TimeProvider` usage pattern already present elsewhere in the codebase (e.g. `CreatePurchaseOrderHandler`, `FlexiManufactureClient`). No DI registration change was needed — `TimeProvider.System` is already registered as a singleton in `ServiceCollectionExtensions.cs`, `AttendanceModule.cs`, and other adapter modules.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — added `TimeProvider _timeProvider` field and constructor parameter; introduced `var now = _timeProvider.GetUtcNow().UtcDateTime;` computed once at the top of `SeedDefaultConfigurationsAsync`; both the create path (`RecurringJobConfiguration` constructor call) and the update path (`existing.UpdateConfiguration(...)`) now use `now` instead of `DateTime.UtcNow`.
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — added `using Microsoft.Extensions.Time.Testing;`, a `FakeTimeProvider _timeProvider` fixture field seeded with a fixed `FixedTime` (`2025-06-01T03:00:00Z`), passed into the seeder's constructor. Added two new assertions:
  - `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`: `Assert.All(configurations, c => Assert.Equal(FixedTime.UtcDateTime, c.LastModifiedAt));`
  - `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`: `Assert.Equal(FixedTime.UtcDateTime, updated.LastModifiedAt);`

## Tests
`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` — covers seeding into an empty database (create path, including the new exact-timestamp assertion), duplicate-prevention, updating `DisplayName`/`Description` on existing rows (update path, including the new exact-timestamp assertion), preserving admin-owned `CronExpression`/`IsEnabled`, and setting `LastModifiedBy` to `"System"`.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```

## Notes
- The task's stated solution path `backend/Anela.Heblo.sln` does not exist in this repo — the actual solution file is at the repo root (`Anela.Heblo.sln`). Used the correct path for Step 5's build/format verification; no repo files were changed because of this.
- In this sandboxed environment, `dotnet test`/`dotnet build` invocations hung indefinitely inside the `GenerateAccessMatrix` MSBuild target (a `BeforeTargets="Build"` target on `Anela.Heblo.API.csproj` that shells out to `dotnet run --project ...AccessMatrixGen`) when using default MSBuild node-reuse/shared-compilation settings — a known nested-`dotnet`-invocation deadlock. Worked around it for all verification commands in this session by passing `-p:UseSharedCompilation=false /nodeReuse:false` plus `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` env vars. This is an environment/tooling workaround only — no project files were changed, and the normal `dotnet build`/`dotnet test` invocations described in the task should work fine in a non-hung environment (e.g. CI).
- Did not add null-guards (`?? throw new ArgumentNullException`) per the task's explicit instruction that this is an out-of-scope style change.
- `artifacts/feat-4043/state.json` showed as modified in `git status` before this task began (pre-existing, unrelated to this change) and was left untouched — only the two files named in the task were staged and committed.

## Status
DONE
