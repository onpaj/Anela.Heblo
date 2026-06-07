Plan complete and saved to `docs/superpowers/plans/2026-06-04-fix-journal-sort-by-author.md`.

Summary of the plan structure (6 tasks, TDD throughout):

1. **Task 1** — Write 4 failing repository tests for `createdByUsername` sort (asc, desc, case-insensitive key, `EntryDate DESC` tiebreaker) + commit.
2. **Task 2** — Extract `ApplySort` + `ApplyDefaultSort` helpers in `JournalRepository`, replace both duplicated switch sites, remove dead `"createdat"` branch, make Task 1 tests pass + commit.
3. **Task 3** — Write 4 tests for warning behavior (1 expects warning, 3 expect silence on null/empty/whitespace). After Task 2, 3 pass and 1 fails — commit.
4. **Task 4** — Add `ApplyDefaultSortWithWarning` with structured `{SortBy}` `{Repository}` log template. Make the failing warning test pass + commit.
5. **Task 5** — Mirror `SearchEntriesAsync` test to lock in the shared helper, preventing future re-duplication + commit.
6. **Task 6** — Validation only: `dotnet build`, `dotnet format --verify-no-changes`, full test pass, confirm no frontend diff, grep that `createdat` is gone.

The plan honors all 5 arch-review amendments (keeps `string sortDirection`, uses `ToLowerInvariant`, uses `IsNullOrWhiteSpace`, includes the mandatory `SearchEntriesAsync` mirror, defers the optional index decision) and maps every spec requirement to a concrete task in the coverage table at the bottom.