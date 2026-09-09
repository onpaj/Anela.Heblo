# Implementation: add-pagination-calculator

## What was implemented

Added `JournalPaginationCalculator`, a shared static helper local to the Journal
feature folder that computes the three pagination metadata fields
(`TotalPages`, `HasNextPage`, `HasPreviousPage`) from `(totalCount, pageNumber,
pageSize)`. This is the extraction point both `GetJournalEntriesHandler` and
`SearchJournalEntriesHandler` will call in the next two tasks to replace their
duplicated inline formulas. Followed strict TDD: wrote the failing test first
(confirmed it failed with `CS0234` — the type didn't exist), then added the
minimal implementation, then reran the test suite to confirm all 7 cases pass.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs` — new `internal static class JournalPaginationCalculator` with a single `Calculate(int totalCount, int pageNumber, int pageSize)` method returning a `(int TotalPages, bool HasNextPage, bool HasPreviousPage)` tuple. Formula is unchanged from the current inline expressions in both handlers (byte-for-byte identical behavior, per spec FR-1).
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs` — new `[Theory]` test with the 7 cases specified in the task context (no rows, single partial page, exact multiple on last page, exact multiple + 1 with more pages, last page of that case, middle page, last page with remainder).

## Tests

- `JournalPaginationCalculatorTests.Calculate_ReturnsExpectedPaginationMetadata` — 7 inline data cases covering boundary conditions (zero rows, exact page-size multiples, remainders, first/middle/last page). All pass.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~JournalPaginationCalculatorTests
```
Result: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

The same `dotnet test` invocation also compiled the full backend dependency
graph (all Adapters, Domain, Application, API projects) with zero errors
(only pre-existing nullable-reference warnings unrelated to this change).

## Notes

- Class is `internal` (per spec's illustrative snippet) — resolves for the
  test project via the existing `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]`
  in `Anela.Heblo.Application/AssemblyInfo.cs`; no new InternalsVisibleTo entry
  needed.
- This task only adds the calculator and its unit test. The two handlers
  (`GetJournalEntriesHandler`, `SearchJournalEntriesHandler`) still contain
  their original inline duplicated formulas — replacing them is explicitly
  the scope of the next two tasks (`refactor-get-journal-entries-handler`,
  `refactor-search-journal-entries-handler`), not this one.
- No public contracts touched (DTOs/requests/responses unchanged, no
  controller/route changes) — consistent with spec FR-2.
- No deviations from the task context's steps.

## PR Summary

Added `JournalPaginationCalculator`, a small internal static helper in the
Journal feature folder that computes `TotalPages`, `HasNextPage`, and
`HasPreviousPage` from `(totalCount, pageNumber, pageSize)`. This is the first
of four tasks deduplicating the pagination-metadata formula currently
copy-pasted between `GetJournalEntriesHandler` and `SearchJournalEntriesHandler`
(issue #4081); the two handlers will be switched over to call this helper in
subsequent tasks. Built with TDD — a 7-case theory test was written first and
confirmed to fail on the missing type before the implementation was added.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/Pagination/JournalPaginationCalculator.cs` — new shared pagination-metadata calculator
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalPaginationCalculatorTests.cs` — new unit tests (7 boundary cases)

## Status
DONE
