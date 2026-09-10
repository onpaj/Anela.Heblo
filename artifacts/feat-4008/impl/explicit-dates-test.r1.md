# Implementation: explicit-dates-test

## What was implemented
Added a new `[Fact]` test method, `Handle_ExplicitDates_PassesThemThroughUnchanged`, to the existing `GetIssuedInvoiceSyncStatsHandlerTests` class. The test covers spec FR-2: when the request carries explicit `FromDate`/`ToDate` values, the handler must pass them through to `IIssuedInvoiceRepository.GetSyncStatsAsync` unchanged, rather than overwriting them with the default trailing-30-day window.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — appended `Handle_ExplicitDates_PassesThemThroughUnchanged`, which arranges explicit `FromDate`/`ToDate` on the request, sets up `_repositoryMock.GetSyncStatsAsync` to expect those exact dates, invokes `_handler.Handle`, and asserts `response.Success` is true and the repository was called exactly once with the explicit dates.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — now contains two tests: the pre-existing `Handle_BothDatesNull_DefaultsToTrailing30DayWindow` (default-date behavior) and the new `Handle_ExplicitDates_PassesThemThroughUnchanged` (explicit-date pass-through behavior).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_ExplicitDates_PassesThemThroughUnchanged"
```
Result observed: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes
The test code was used verbatim from the task context file — it compiled and passed without any adaptation, since `GetIssuedInvoiceSyncStatsRequest.FromDate`/`ToDate`, `IIssuedInvoiceRepository.GetSyncStatsAsync(DateTime, DateTime, CancellationToken)`, and `IssuedInvoiceSyncStats` all matched the signatures assumed by the task spec. No deviations.

## PR Summary
Adds unit test coverage confirming `GetIssuedInvoiceSyncStatsHandler` passes explicit `FromDate`/`ToDate` request values straight through to the repository call instead of silently overwriting them with the default trailing-30-day window, closing the FR-2 coverage gap alongside the already-existing default-window test.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — added `Handle_ExplicitDates_PassesThemThroughUnchanged` test

## Status
DONE
