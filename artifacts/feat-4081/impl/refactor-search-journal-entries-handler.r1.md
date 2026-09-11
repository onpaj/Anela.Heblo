# Implementation: refactor-search-journal-entries-handler

## What was implemented
Added regression-locking pagination-metadata tests to `SearchJournalEntriesHandlerTests`, confirmed they passed against the old inline pagination formula, then refactored `SearchJournalEntriesHandler` to delegate `TotalPages`/`HasNextPage`/`HasPreviousPage` computation to the shared `JournalPaginationCalculator.Calculate(...)` (already introduced by the prior `add-pagination-calculator` task and already used by `GetJournalEntriesHandler`), eliminating the duplicated inline formula. Re-ran the tests to confirm identical behavior post-refactor.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — replaced the inline `TotalPages`/`HasNextPage`/`HasPreviousPage` calculations with a call to `JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize)` (namespace `Anela.Heblo.Application.Features.Journal.Pagination`), matching the pattern already used in the sibling `GetJournalEntriesHandler.cs`. Matches the exact content specified in the task.
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — added two new `[Fact]` tests: `Handle_MiddlePage_ReturnsCorrectPaginationMetadata` (page 2 of 3, TotalCount=25, PageSize=10 → TotalPages=3, HasNextPage=true, HasPreviousPage=true) and `Handle_FirstPageExactMultiple_HasNoNextOrPreviousPage` (page 1, TotalCount=10, PageSize=10 → TotalPages=1, HasNextPage=false, HasPreviousPage=false).

## Tests
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — now 6 tests total (4 pre-existing + 2 new), covering product-code-prefix search, raw content passthrough, and pagination metadata (middle page and exact-multiple/last-page edge case).

Verification runs (via `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SearchJournalEntriesHandlerTests`):
- Before refactor: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6` (confirms the new tests lock in the pre-refactor inline-formula behavior).
- After refactor: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6` (confirms the refactor produced byte-identical output to the old inline formula).

Also ran `dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore` (0 errors, only pre-existing nullable warnings unrelated to this change) and `dotnet format Anela.Heblo.sln --no-restore --include <the two changed files>` (no changes made — files already conform to style).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~SearchJournalEntriesHandlerTests
```
Expect: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Notes
Confirmed `JournalPaginationCalculator` lives at `Anela.Heblo.Application.Features.Journal.Pagination.JournalPaginationCalculator` (file `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs`) with signature `public static (int TotalPages, bool HasNextPage, bool HasPreviousPage) Calculate(int totalCount, int pageNumber, int pageSize)` — this matches the task's snippet exactly (namespace, method name, and destructuring call site), so no deviation from the provided refactor snippet was needed. The sibling `GetJournalEntriesHandler.cs` was inspected and calls it identically. No other files were touched. A full-solution `dotnet build` on the whole `.sln` fails due to several unrelated adapter/tool test projects (`Anela.Heblo.Adapters.Flexi.Tests`, `.Shoptet.Tests`, `.HomeAssistant.Tests`, etc.) missing `obj/project.assets.json` (pre-existing, unrestored in this checkout, unrelated to Journal) — this is a pre-existing environment/restore gap, not caused by this change; the Application project and the Journal test project both build and test cleanly on their own.

## PR Summary
Refactors `SearchJournalEntriesHandler` to use the shared `JournalPaginationCalculator` (introduced earlier in this feature for `GetJournalEntriesHandler`) instead of duplicating the `TotalPages`/`HasNextPage`/`HasPreviousPage` formula inline. Adds two new unit tests covering pagination-metadata edge cases (middle page, and last-page-exact-multiple) that were previously untested, and used them as a before/after regression check to prove the refactor is behavior-preserving.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs`

## Status
DONE
