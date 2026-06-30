# Implementation: fix-exception-leakage

## What was implemented

Removed `ex.Message` from API error responses in three Analytics handlers to prevent leakage of internal details (PostgreSQL connection strings, EF Core queries, etc.). Added `ILogger<T>` injection to each handler so exceptions are still logged server-side. Updated unit tests to pass the new logger dependency and corrected assertions that expected the removed `"details"` param.

## Files created/modified

**Production code:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — added `ILogger<GetMarginReportHandler>` field + constructor param; catch block now calls `_logger.LogError(ex, ...)` and returns `CreateErrorResponse(ErrorCodes.InternalServerError)` with no params
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — same pattern as above for `GetProductMarginAnalysisHandler`
- `backend/src/Anela.Heblo.Application/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTile.cs` — added `ILogger<InvoiceImportStatisticsTile>` field + constructor param; catch block now calls `_logger.LogError(ex, ...)` and returns anonymous object without `details` field

**Test code (constructor and assertion fixes):**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` — pass `NullLogger<GetMarginReportHandler>.Instance`; removed `.Params.Should().ContainKey("details")` assertion from `Handle_RepositoryException_ReturnsInternalServerError`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` — same as above for `GetProductMarginAnalysisHandler`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/DashboardTiles/InvoiceImportStatisticsTileTests.cs` — pass `NullLogger<InvoiceImportStatisticsTile>.Instance` to constructor

## Tests

Existing unit tests cover all three handlers including the exception path (`Handle_RepositoryException_ReturnsInternalServerError`). Those tests were updated to reflect the behaviour change (no `"details"` key in params). No new tests written — this is a pure security refactor with no logic change.

## How to verify

1. `dotnet build Anela.Heblo.sln` — must succeed (confirmed: Build succeeded)
2. `dotnet format Anela.Heblo.sln --verify-no-changes` — must produce no output (confirmed: clean)
3. Trigger an exception in a dev/staging environment and confirm the API response body contains only `ErrorCode: InternalServerError` with no `details` field, while the application log contains the full exception.

## Notes

- `CreateErrorResponse` uses `params (string key, string value)[] parameters` so calling it with zero args is valid — no overload needed.
- `NullLogger<T>.Instance` is the standard .NET testing pattern; no extra test dependency required (it's in `Microsoft.Extensions.Logging.Abstractions` which is already transitively referenced).
- The task description said to modify only 3 files, but the 3 corresponding test files also needed updating to compile. All 6 files are in scope of this fix.

## PR Summary

Fixes a security issue where three Analytics handlers leaked internal exception details (`ex.Message`) in API responses, potentially exposing PostgreSQL connection strings and EF Core query text. ILogger is now injected into each handler; exceptions are logged server-side and a generic `InternalServerError` code is returned to the client.

### Changes
- `GetMarginReportHandler.cs` — inject ILogger, log exception, remove ex.Message from response
- `GetProductMarginAnalysisHandler.cs` — inject ILogger, log exception, remove ex.Message from response
- `InvoiceImportStatisticsTile.cs` — inject ILogger, log exception, remove details field from error response
- `GetMarginReportHandlerTests.cs` — pass NullLogger; fix assertion
- `GetProductMarginAnalysisHandlerTests.cs` — pass NullLogger; fix assertion
- `InvoiceImportStatisticsTileTests.cs` — pass NullLogger to constructor

## Status
DONE
