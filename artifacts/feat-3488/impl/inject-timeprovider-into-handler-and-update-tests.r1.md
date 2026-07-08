# Implementation: inject-timeprovider-into-handler-and-update-tests

## What was implemented
`GetInvoiceImportStatisticsHandler` now computes `endDate` from an injected `TimeProvider` instead of calling `DateTime.UtcNow` directly, matching the pattern already established by the sibling `GetBankStatementImportStatisticsHandler`, `TimeWindowParser`, and `InvoiceImportStatisticsTile` in the same Analytics module. The handler's unit tests were updated to construct the handler with a mocked `TimeProvider` returning a fixed date instead of relying on wall-clock time.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — added a `TimeProvider _timeProvider` field and constructor parameter; `Handle()` now reads `_timeProvider.GetUtcNow().Date` instead of `DateTime.UtcNow.Date`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — added a `Mock<TimeProvider>` stubbed to return a fixed `DateTimeOffset` (2025-10-14T10:00:00Z), threaded `_timeProviderMock.Object` through all 4 handler construction sites, and rebased the two date-dependent assertions onto the fixed date instead of `DateTime.UtcNow.Date`.

## Tests
`backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — covers threshold logic, default configuration, configurable days-back range (asserted against the fixed date), date-type pass-through, and default option values. All 6 tests are now deterministic (no wall-clock dependency).

## How to verify
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"
```
Expected: build succeeds, 6/6 tests pass.

## Notes
- No DI registration changes were needed — `TimeProvider.System` is already registered as a singleton in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, and MediatR resolves the handler via assembly scan.
- A full `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` run shows 64 pre-existing failures across the suite, all due to `Docker is either not running or misconfigured` (Testcontainers-based PostgreSQL integration tests) — a sandbox environment limitation unrelated to this change. 5588 tests pass, including all Analytics-related tests.
- `dotnet format` was run against both changed files; it made no additional changes.

## PR Summary
Fixes an arch-review finding: `GetInvoiceImportStatisticsHandler` called `DateTime.UtcNow` directly instead of using the injected `TimeProvider` already used elsewhere in the Analytics module, making the handler's date range non-deterministic in tests. The fix injects `TimeProvider` via the constructor (already registered in DI) and replaces the direct call with `_timeProvider.GetUtcNow().Date`. The existing unit tests were updated to mock `TimeProvider` with a fixed date so they no longer depend on wall-clock time.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs` — inject and use `TimeProvider`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs` — mock `TimeProvider` with a fixed date across all handler construction sites

## Status
DONE
