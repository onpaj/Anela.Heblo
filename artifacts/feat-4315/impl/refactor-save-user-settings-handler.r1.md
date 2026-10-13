# Implementation: refactor-save-user-settings-handler

## What was implemented

Thinned `SaveUserSettingsHandler` down to a pure caller of `IUserDashboardSettingsMutator.MutateBulkAsync`,
removing its direct dependencies on `IUserDashboardSettingsRepository`, `IUserDashboardSettingsLock`,
`TimeProvider`, and `IMediator` — all of that scaffold now lives once, in the shared mutator
(`UserDashboardSettingsMutator`, added by the `implement-mutate-bulk-async` task). The handler's test
file was rewritten to mock `IUserDashboardSettingsMutator` directly instead of the repository/lock/
mediator/time-provider quartet, matching the task-context spec exactly.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` — rewritten to depend only on `IUserDashboardSettingsMutator` and `ICurrentUserService`; delegates to `MutateBulkAsync(userId, request.Tiles ?? [], cancellationToken)`.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` — replaced in full with the 5-test suite from the task context (mutator-call assertions, null-tiles handling, anonymous/empty user id pass-through, success response, single-call verification).

## Deviation from task context (required to compile)

The task context's Step 3 handler snippet declares `public class SaveUserSettingsHandler`, but
`IUserDashboardSettingsMutator` is `internal` (declared in `IUserDashboardSettingsMutator.cs`). A
public class cannot expose an internal type as a constructor parameter (CS0051: inconsistent
accessibility). The two sibling handlers that already depend on this same interface —
`EnableTileHandler` and `DisableTileHandler` — are both declared `internal sealed class` for exactly
this reason. I changed `SaveUserSettingsHandler` from `public class` to `internal sealed class` to
match that established, already-reviewed pattern. No other change was needed (MediatR resolves
handlers by reflection regardless of accessibility, and the test project already has
`InternalsVisibleTo` covering the two existing internal handlers' tests).

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` (5 tests, all
  passing): delegation with correct userId/tiles, null-tiles → empty list, null/empty userId passed
  through unchanged (mutator owns "anonymous" normalization), always-success response, mutator called
  exactly once.

## How to verify

```bash
cd backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SaveUserSettingsHandlerTests
```

Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

Full backend suite (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, no filter) also ran:
`Failed: 111, Passed: 7872, Skipped: 4, Total: 7987`. All 111 failures are pre-existing
Testcontainers/PostgreSQL integration tests failing with `System.ArgumentException: Docker is either
not running or misconfigured` — this sandbox has no Docker daemon. None of the 111 touch
`Dashboard`/`SaveUserSettings`/`UserDashboardSettingsMutator`; they are unrelated Bank/Article/etc.
integration suites that fail identically regardless of this change (the shared
`PostgresSharedContainerFixture` throws at construction, before any test body runs). This is an
environment limitation, not a regression introduced here.

`dotnet format --verify-no-changes` scoped to the two files this task touched: clean, no changes
needed. (A solution-wide `dotnet format --verify-no-changes` run also surfaces pre-existing whitespace
violations in unrelated `MarketingPerformance` test files — untouched by this task, not a regression.)

## Notes

- Per CLAUDE.md's "surgical changes" rule, I did not touch the unrelated pre-existing
  `MarketingPerformance` test formatting issues or attempt to fix the Docker/Testcontainers
  environment gap — both are out of scope for this task.
- This was the third and final dev task for feat-4315 (`add-mutate-bulk-async-interface` and
  `implement-mutate-bulk-async` were completed in earlier units); all three tasks' spec coverage is
  cross-checked in the task-context file's own Self-Review section.

## PR Summary

Thinned `SaveUserSettingsHandler` down to a single call into the shared `IUserDashboardSettingsMutator.MutateBulkAsync`, removing its direct repository/lock/mediator/time-provider dependencies — completing the de-duplication of the dashboard settings mutation scaffold that `EnableTileHandler`/`DisableTileHandler` already used. Rewrote the handler's unit tests to mock the mutator directly.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` — thinned to `IUserDashboardSettingsMutator` + `ICurrentUserService`; made `internal sealed` to match sibling handlers (required for the internal mutator dependency to compile)
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` — rewritten to mock `IUserDashboardSettingsMutator`

## Status
DONE
