Final review: **READY TO MERGE**. Writing the output artifact.

---

# Implementation: Fix Journal List Sort by Author (CreatedByUsername)

## What was implemented

Fixed the Journal list "Autor" column sort bug: the backend was silently falling back to `EntryDate` ordering when `sortBy=createdByUsername` was sent from the frontend. Added the missing backend case, consolidated the two duplicated sort switches into a single `ApplySort` helper, removed the dead `createdat` branch, and added structured warning logging for unknown sort keys.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — replaced both inline sort switches with calls to a new `private static ApplySort` helper; added `"createdbyusername"` case with `ThenByDescending(EntryDate)` tiebreaker; removed dead `"createdat"` branch; added `ApplyDefaultSortWithWarning` with structured `{SortBy}`/`{Repository}` log properties
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — added 9 new tests: 4 for `createdByUsername` sort (asc, desc, case-insensitive key, tiebreaker), 4 for warning behavior (unknown key logs once; null/empty/whitespace are silent), 1 `SearchEntriesAsync` mirror

## Tests

- `JournalRepositoryIntegrationTests` — 17 tests total (9 new + 8 pre-existing), all passing
  - `GetEntriesAsync_SortsByCreatedByUsername_Ascending`
  - `GetEntriesAsync_SortsByCreatedByUsername_Descending`
  - `GetEntriesAsync_SortsByCreatedByUsername_AcceptsAnyCasing`
  - `GetEntriesAsync_SortByCreatedByUsername_TiebreaksByEntryDateDesc`
  - `GetEntriesAsync_UnknownSortBy_LogsWarningWithStructuredProperty`
  - `GetEntriesAsync_NullSortBy_DoesNotLogWarning`
  - `GetEntriesAsync_EmptySortBy_DoesNotLogWarning`
  - `GetEntriesAsync_WhitespaceSortBy_DoesNotLogWarning`
  - `SearchEntriesAsync_SortsByCreatedByUsername_Ascending`

## How to verify

```bash
cd backend
dotnet build --nologo --verbosity minimal
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalRepositoryIntegrationTests" --nologo --verbosity minimal
```

## Notes

- No frontend changes required — `JournalList.tsx` already sends `column="createdByUsername"` which maps correctly to the new backend case.
- No EF Core migration needed — `CreatedByUsername` column already exists.
- The `logger` parameter was added to `ApplySort` in Task 2 (before it was used) so the signature didn't need to change in Task 4. Build confirmed no warnings-as-errors active.
- All 5 arch-review amendments honored: `string sortDirection` (not `bool`), `ToLowerInvariant`, `IsNullOrWhiteSpace`, mandatory `SearchEntriesAsync` mirror, index deferred.

## PR Summary

Fixed the Journal list "Autor" column sort: the frontend already sent `sortBy=createdByUsername` but the backend had no matching case and silently fell through to `EntryDate` ordering. Users clicking the column header would see rows shuffle (because EntryDate changed) but not actually sort by author — a silent UX defect with no error or log.

The fix consolidates the two duplicated sort switches in `JournalRepository` (one in `GetEntriesAsync`, one in `SearchEntriesAsync`) into a single `private static ApplySort` helper — the duplication was precisely how the two switches drifted independently and allowed the bug to exist. The helper adds the `"createdbyusername"` case with a `ThenByDescending(EntryDate)` tiebreaker for deterministic pagination, removes the dead `"createdat"` branch (no frontend ever sends this), and emits a structured `LogWarning` with `{SortBy}` property when an unrecognized non-empty key arrives — making future frontend/backend contract drift observable in Application Insights instead of invisible.

### Changes
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — extracted `ApplySort`/`ApplyDefaultSort`/`ApplyDefaultSortWithWarning` helpers; added `createdbyusername` case; removed dead `createdat` case
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — 9 new tests covering sort correctness, case-insensitivity, tiebreaker, warning/silence behavior, and both call-site paths

## Status

DONE