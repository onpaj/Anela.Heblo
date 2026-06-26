Plan saved to `docs/superpowers/plans/2026-06-04-extract-journal-repository-sorting.md`.

**Summary of the plan:**

- **Task 1 (RED):** Add a `[Theory]` test pinning the 12-case sort matrix for `GetEntriesAsync` against the *unrefactored* code, then commit it as a baseline.
- **Task 2 (RED):** Same matrix for `SearchEntriesAsync`, reusing the shared `SortMatrix` member data and `SeedSortFixtureAsync` helper.
- **Task 3 (GREEN):** Replace both inline `switch` blocks (lines 43–55 and 134–146) with a single call to a new `private static ApplySorting(IQueryable<JournalEntry>, string?, string)` helper placed after `GetJournalIndicatorsAsync`. Verified by a grep check that `"title"` and `"createdat"` each appear exactly once in the file.
- **Task 4:** Final validation gate — solution-wide `dotnet build`, `dotnet format --verify-no-changes`, full Journal test run, plus a diff spot-check for surgical scope.

The plan includes a spec-coverage table at the bottom mapping every FR/NFR (including Architecture Amendments 1 and 3) to specific tasks/steps. Test fixture values are chosen so each `(sortBy, sortDirection)` combination produces a distinct row order, making the assertions unambiguous.