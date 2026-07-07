# Implementation: inject-timeprovider-bank-statement-handler

## What was implemented
`GetBankStatementImportStatisticsHandler` now takes a `TimeProvider` constructor dependency and uses `_timeProvider.GetUtcNow().Date` instead of calling `DateTime.UtcNow.Date` directly, matching the established pattern in `InvoiceImportStatisticsTile` and `TimeWindowParser`. No DI registration changes were needed since `TimeProvider.System` is already registered as a singleton. Added unit test coverage proving the default-date branch is now deterministically testable via a mocked `TimeProvider`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs` — added `TimeProvider` constructor parameter and `_timeProvider` field; replaced `DateTime.UtcNow.Date` with `_timeProvider.GetUtcNow().Date`. No other lines changed.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs` — new test file (none existed before) with two tests: default-date-range resolution via mocked `TimeProvider`, and pass-through behavior confirming the provider is not consulted when explicit dates are supplied.

## Tests
- `GetBankStatementImportStatisticsHandlerTests.Handle_WithNoDatesProvided_UsesInjectedTimeProviderForDefaultRange` — passes.
- `GetBankStatementImportStatisticsHandlerTests.Handle_WithExplicitDatesProvided_DoesNotConsultTimeProvider` — passes.
- Full `Features.Analytics` test folder (99 tests, including sibling `GetInvoiceImportStatisticsHandlerTests` and `InvoiceImportStatisticsTileTests`) — all pass, no regressions.
- `dotnet build` on `Anela.Heblo.sln` — 0 errors (13 pre-existing warnings in unrelated Flexi adapter test files, not touched by this change).
- `dotnet format` on the two changed files — no formatting changes required.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankStatementImportStatisticsHandlerTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Analytics"
dotnet build ../Anela.Heblo.sln
```

## Notes
No deviations from the plan. The DI container resolves the new `TimeProvider` constructor parameter automatically via the existing `services.AddSingleton(TimeProvider.System)` registration — confirmed no other callers of this handler needed changes.

## PR Summary
`GetBankStatementImportStatisticsHandler` called `DateTime.UtcNow.Date` directly to compute its default end-date, unlike its sibling module code (`InvoiceImportStatisticsTile`, `TimeWindowParser`) which already inject `TimeProvider`. This made the default-date branch — the common real-world path, since the frontend doesn't pass an explicit end date — untestable without a time-freezing shim.

This change injects `TimeProvider` via the constructor (no DI registration change needed) and swaps the direct clock call for `_timeProvider.GetUtcNow().Date`. Behavior is unchanged in production (`TimeProvider.System` wraps the real clock). A new test file adds coverage for both the default-date branch (via a mocked fixed time) and the explicit-date pass-through path.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs` — inject `TimeProvider`, replace `DateTime.UtcNow.Date` with `_timeProvider.GetUtcNow().Date`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs` — new test file covering both the default-date and explicit-date paths.

## Status
DONE
