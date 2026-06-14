The final reviewer approved with a medium note about a pre-existing nullable annotation mismatch that the spec explicitly puts out of scope (no public API changes). The implementation is complete.

---

# Implementation: Extract Duplicated Journal Sorting Logic

## What was implemented

Eliminated the verbatim duplicated sort `switch` expression in `JournalRepository` by:
1. Adding 24 sort-matrix baseline tests (12 per method) to pin current behavior before the refactor
2. Extracting a single `private static ApplySorting(IQueryable<JournalEntry>, string?, string)` helper at the bottom of `JournalRepository`
3. Replacing both inline `switch` blocks (in `GetEntriesAsync` and `SearchEntriesAsync`) with one-line calls to the helper

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — removed two identical 13-line sort switch blocks; added `ApplySorting` private static helper at bottom of class
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — added `SeedSortFixtureAsync`, `SortMatrix` (12 cases), `GetEntriesAsync_AppliesExpectedOrdering`, and `SearchEntriesAsync_AppliesExpectedOrdering`

## Tests

- `JournalRepositoryIntegrationTests.cs` — 32 tests total (5 pre-existing + 12 GetEntriesAsync matrix + 12 SearchEntriesAsync matrix + 3 GetJournalIndicators). All pass.
- Sort matrix covers: `"title"`, `"createdat"`, `"TITLE"` (case-insensitive), `"unknown"`, `null` × `"ASC"`, `"DESC"`, `"weird"` for both repository methods.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~JournalRepositoryIntegrationTests"
grep -n '"title"\|"createdat"' src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs
# Each should appear exactly once (in ApplySorting only)
```

## Notes

The final reviewer flagged a medium-severity nullable mismatch: `ApplySorting` declares `string? sortBy` while `IJournalRepository` declares `string sortBy`. This is a pre-existing API roughness made visible by the extraction. The spec explicitly states "No changes to public API" and the runtime behavior is correct (EF Core's `?.ToLower()` handles null safely). Fixing it would require making `sortBy` nullable on the interface, which is out of scope per the spec. The `null!` suppressions in the sort-matrix tests are the only observable symptom.

## PR Summary

Eliminated the verbatim duplicated sort `switch` expression in `JournalRepository` by extracting it into a single `private static ApplySorting` helper, following the pattern already established in `IssuedInvoiceRepository`. Any future addition of a sortable column now requires a single edit. Twenty-four new sort-matrix tests (12 per repository method) pin the behavior before and after, ensuring the refactor introduced no ordering regression.

### Changes
- `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` — removed two identical 13-line inline switch blocks; added `private static ApplySorting` helper at bottom of class
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — added `SeedSortFixtureAsync`, `SortMatrix`, `GetEntriesAsync_AppliesExpectedOrdering`, and `SearchEntriesAsync_AppliesExpectedOrdering`

## Status
DONE