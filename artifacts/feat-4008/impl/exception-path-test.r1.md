# Implementation: exception-path-test

## What was implemented
Added a new `[Fact]` test method, `Handle_RepositoryThrows_ReturnsStructuredFailure`, to the existing `GetIssuedInvoiceSyncStatsHandlerTests` class. The test covers spec FR-3: when the repository throws during `GetSyncStatsAsync`, the handler must catch the exception and return a structured failure response — not rethrow — with `Success = false`, `ErrorCode = ErrorCodes.Exception`, the exact Czech `Params["ErrorMessage"]` text, and all numeric/date fields left at their unset defaults.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — appended `Handle_RepositoryThrows_ReturnsStructuredFailure`, which arranges the repository mock to throw `InvalidOperationException`, invokes `_handler.Handle`, and asserts the full structured-failure response shape (Success, ErrorCode, Params["ErrorMessage"], and the zeroed/null stat fields), confirming the handler does not rethrow.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — now contains three tests: `Handle_BothDatesNull_DefaultsToTrailing30DayWindow`, `Handle_ExplicitDates_PassesThemThroughUnchanged`, and the new `Handle_RepositoryThrows_ReturnsStructuredFailure`.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_RepositoryThrows_ReturnsStructuredFailure"
```
Result observed: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3` (full fixture run, filter matched the whole class since VSTest resolved by name prefix).

## Notes
The test code was used verbatim from the task context file — it compiled and passed without adaptation; the handler's catch block (`GetIssuedInvoiceSyncStatsHandler.cs`) matches the asserted shape exactly (`ErrorCodes.Exception`, `Params["ErrorMessage"] = "Chyba při načítání statistik synchronizace faktur"`, response fields left at their type defaults). No deviations.

Environment note: the local `dotnet test` run intermittently hung inside the API project's `GenerateAccessMatrix` MSBuild target (a nested `dotnet run` invoked via `<Exec>` in `Anela.Heblo.API.csproj`), which can deadlock under MSBuild node reuse. Running `dotnet build-server shutdown` followed by `MSBUILDDISABLENODEREUSE=1 dotnet test ...` reliably unblocked it. No source changes were made to work around this — it is a pre-existing build-target characteristic, unrelated to this task's test addition.

## PR Summary
Adds unit test coverage for `GetIssuedInvoiceSyncStatsHandler`'s exception path: when the repository throws, the handler must return a structured failure response (`Success = false`, `ErrorCode = ErrorCodes.Exception`, the exact Czech error message, and zeroed/null stat fields) rather than letting the exception propagate. Closes the FR-3 coverage gap.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — added `Handle_RepositoryThrows_ReturnsStructuredFailure` test

## Status
DONE
