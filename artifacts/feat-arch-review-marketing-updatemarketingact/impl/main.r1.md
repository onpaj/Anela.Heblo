---

# Implementation: Marketing Module — Consistent DB Save Error Handling for Update and Delete Handlers

## What was implemented

Added a `try/catch (Exception)` guard around the DB save calls in `UpdateMarketingActionHandler` and `DeleteMarketingActionHandler`, mirroring the pattern already in `CreateMarketingActionHandler`. On DB failure after a successful Outlook write, each handler now logs a structured `LogError` and returns `ErrorCodes.DatabaseError` instead of letting the exception surface as HTTP 500.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs` — wrapped `UpdateAsync` + `SaveChangesAsync` in try/catch; logs `{ActionId}`, `{EventId}`, greppable phrase "may now be out of sync"; returns `ErrorCodes.DatabaseError`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs` — wrapped `DeleteSoftAsync` in try/catch; logs `{ActionId}`, `{EventId}`, greppable phrase "already deleted — DB row still present"; returns `ErrorCodes.DatabaseError`
- `backend/test/Anela.Heblo.Tests/Application/Marketing/UpdateMarketingActionHandlerTests.cs` — added `Handle_ReturnsDatabaseError_WhenDbSaveFails` test; asserts `Success == false`, `ErrorCode == DatabaseError`, Outlook update called once, no Outlook rollback, error log contains "may now be out of sync"
- `backend/test/Anela.Heblo.Tests/Application/Marketing/DeleteMarketingActionHandlerTests.cs` — added `Handle_ReturnsDatabaseError_WhenDbDeleteFails` test; asserts `Success == false`, `ErrorCode == DatabaseError`, Outlook delete called once, error log contains "already deleted"

## Tests

All 27 Marketing handler tests pass (`dotnet test --filter "FullyQualifiedName~Application.Marketing"` — 0 failures, 0 skipped).

- `UpdateMarketingActionHandlerTests` — 11 tests including new DB-failure test
- `DeleteMarketingActionHandlerTests` — 9 tests including new DB-failure test
- `CreateMarketingActionHandlerTests` — 7 tests, all unchanged and still passing

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Application.Marketing"
# Expected: 27 passed, 0 failed
```

## Notes

- The solution-wide build fails due to missing NuGet restore assets in the worktree (pre-existing, not caused by this change). The Application project and test project both build cleanly.
- Integration tests were not added — no existing `WebApplicationFactory`-based harness covers Marketing endpoints, per the arch-review specification amendment.
- `CreateMarketingActionHandler` is untouched (FR-4).

## PR Summary

Added guarded DB save error handling to `UpdateMarketingActionHandler` and `DeleteMarketingActionHandler` to match the existing pattern in `CreateMarketingActionHandler`. Previously, a DB failure after a successful Outlook write in these two handlers would propagate as an unhandled HTTP 500 and leave Outlook and the database silently diverged. Now both handlers catch the exception, log a structured error (with `{ActionId}`, `{EventId}`, and a greppable phrase), and return a `DatabaseError` response envelope.

### Changes
- `UpdateMarketingActionHandler.cs` — try/catch around `UpdateAsync` + `SaveChangesAsync`; `LogError` with "may now be out of sync"; returns `ErrorCodes.DatabaseError`
- `DeleteMarketingActionHandler.cs` — try/catch around `DeleteSoftAsync`; `LogError` with "already deleted — DB row still present"; returns `ErrorCodes.DatabaseError`
- `UpdateMarketingActionHandlerTests.cs` — `Handle_ReturnsDatabaseError_WhenDbSaveFails` covering response shape, no Outlook rollback, and log assertion
- `DeleteMarketingActionHandlerTests.cs` — `Handle_ReturnsDatabaseError_WhenDbDeleteFails` covering response shape, Outlook delete called once, and log assertion

## Status

DONE