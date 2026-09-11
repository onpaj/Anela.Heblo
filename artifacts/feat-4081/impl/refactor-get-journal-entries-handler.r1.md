# Implementation: refactor-get-journal-entries-handler

## What was implemented

Refactored `GetJournalEntriesHandler` to call the shared
`JournalPaginationCalculator.Calculate` helper (added in the prior
`add-pagination-calculator` task) instead of computing `TotalPages`,
`HasNextPage`, and `HasPreviousPage` inline. This removes the second of the
two duplicated copies of the pagination-metadata formula (issue #4081); the
other copy in `SearchJournalEntriesHandler` is out of scope for this task
(covered by `refactor-search-journal-entries-handler`).

Followed the task context precisely: wrote the two-case handler test first
(pre-existing behavior, confirmed it already passed against the unmodified
handler as the spec's safety-net step describes), then replaced the inline
formula with a call to the shared calculator, then re-ran the same test to
confirm byte-identical output.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs` — added `using Anela.Heblo.Application.Features.Journal.Pagination;` and replaced the three inline pagination expressions with `JournalPaginationCalculator.Calculate(result.TotalCount, request.PageNumber, request.PageSize)`, deconstructed into `(totalPages, hasNextPage, hasPreviousPage)` and assigned to the response. No other lines changed.
- `backend/test/Anela.Heblo.Tests/Features/Journal/GetJournalEntriesHandlerTests.cs` — new file (none existed before for this handler), exactly as specified in the task context: `Handle_MiddlePage_ReturnsCorrectPaginationMetadata` (TotalCount=25, PageNumber=2, PageSize=10 → TotalPages=3, HasNextPage=true, HasPreviousPage=true) and `Handle_FirstPageExactMultiple_HasNoNextOrPreviousPage` (TotalCount=10, PageNumber=1, PageSize=10 → TotalPages=1, HasNextPage=false, HasPreviousPage=false).

## Tests

- `GetJournalEntriesHandlerTests.Handle_MiddlePage_ReturnsCorrectPaginationMetadata` — middle-page boundary case.
- `GetJournalEntriesHandlerTests.Handle_FirstPageExactMultiple_HasNoNextOrPreviousPage` — exact-multiple/last-page boundary case (no next, no previous).

Both pass before and after the refactor, confirming behavior-preserving output.

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~GetJournalEntriesHandlerTests --no-restore
```

Results:
- `dotnet build` on `Anela.Heblo.Application` (and its `Anela.Heblo.Domain` dependency): 0 errors, 133 pre-existing warnings, none in Journal files touched by this change.
- `dotnet test --filter FullyQualifiedName~GetJournalEntriesHandlerTests`: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`. The full test project (`Anela.Heblo.Tests.csproj`, which covers every feature module) also compiled cleanly with 0 errors under this change.

Note: per the task context, `full-suite-validation` (running the entire `dotnet test` suite, `dotnet build`, and `dotnet format --verify-no-changes` across the whole solution) is a separate, later task in this plan — not part of this task's scope.

## Notes

- No deviations from the task context's steps.
- No public contracts touched: `GetJournalEntriesRequest`/`GetJournalEntriesResponse` are both unchanged (both remain classes, no new/removed/reordered properties).
- `JournalPaginationCalculator` is `internal` and already covered by the existing `InternalsVisibleTo("Anela.Heblo.Tests")` — no visibility issue when referencing it from the handler (same assembly) or from the test project.
- `SearchJournalEntriesHandler` intentionally left untouched — its inline formula is the explicit scope of the next task (`refactor-search-journal-entries-handler`), not this one.

## PR Summary

Refactored `GetJournalEntriesHandler` to use the shared `JournalPaginationCalculator.Calculate` helper (added in the prior task) instead of its own inline pagination-metadata formula, removing one of the two duplicated copies targeted by issue #4081. Behavior is unchanged — a new two-case unit test suite for the handler (previously untested) passes identically before and after the refactor, and the full backend build/test-project compile remains clean.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/GetJournalEntries/GetJournalEntriesHandler.cs` — replaced inline pagination-metadata calculation with a call to `JournalPaginationCalculator.Calculate`
- `backend/test/Anela.Heblo.Tests/Features/Journal/GetJournalEntriesHandlerTests.cs` — new tests covering middle-page and exact-multiple/last-page pagination metadata

## Status
DONE
