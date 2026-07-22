# Implementation: add-paged-sort-and-paging-tests

## What was implemented

Appended 6 new test methods to the existing integration test file
`LeafletDocumentRepositoryPagedTests.cs`, completing coverage of
`LeafletDocumentRepository.GetDocumentsPagedAsync`'s four-way sort switch
(`Filename` / `Status` / `IndexedAt` / default-to-`IngestedAt`) in both ascending
and descending directions, plus page-slicing and total-count behavior. The
tests reuse the file's existing `MakeDocument` helper (no duplication) and
follow the file's existing Arrange/Act/Assert style.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — added 6 test methods (5 `[Fact]` + 1 `[Theory]` with 2 cases) after the existing `GetDocumentsPagedAsync_AllFiltersCombined_AndSemantics` method, before the class's closing brace. No other changes to the file.

## Tests

- `GetDocumentsPagedAsync_SortByFilename_BothDirections` — seeds 3 docs with distinct filenames, asserts ascending/descending order by `Filename`.
- `GetDocumentsPagedAsync_SortByStatus_BothDirections` — seeds one doc per `LeafletDocumentStatus` value, asserts ordering follows the enum ordinal (`Processing=0 < Indexed=1 < Failed=2`) ascending and descending.
- `GetDocumentsPagedAsync_SortByIndexedAt_BothDirections_WithNulls` — seeds two docs with distinct `IndexedAt` values and one with `IndexedAt = null`; asserts Postgres's default NULLS placement (NULLS LAST ascending, NULLS FIRST descending).
- `GetDocumentsPagedAsync_UnrecognizedSortBy_FallsBackToIngestedAt` (`[Theory]`, cases `""` and `"NotARealColumn"`) — asserts the switch's `_ =>` arm falls back to `IngestedAt` ordering regardless of an empty or unrecognized `sortBy`.
- `GetDocumentsPagedAsync_PageSlicing_StableTotal` — seeds 5 docs, pages through with `pageSize: 2` across 3 pages, asserts `Total` stays 5 on every page and each page returns the correct slice (page 3 has 1 item).
- `GetDocumentsPagedAsync_Total_ReflectsFilteredCount_NotPagedCount` — seeds 5 docs (3 matching a content-type filter), requests `pageSize: 2`, asserts `items.Count == 2` but `total == 3` (the filtered count, not the page size).

## How to verify

```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests"
```
Expects 15 passing (8 pre-existing filter tests + 7 new sort/paging cases: SortByFilename(1) + SortByStatus(1) + SortByIndexedAt(1) + UnrecognizedSortBy theory(2) + PageSlicing(1) + ReflectsFilteredCount(1) = 7).

This requires a running Docker daemon (Testcontainers spins up a `pgvector/pgvector:pg16` Postgres container) and was **not** run in this sandbox — see Notes.

Also verified in this session:
```
timeout 240 dotnet build Anela.Heblo.sln     # 0 errors, 251 pre-existing warnings unrelated to this file
timeout 180 dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs   # no output = clean
```

## Notes

- **Docker/Testcontainers limitation (known, accepted)**: this sandbox has no running Docker daemon, so `dotnet test` against these `[Trait("Category", "Integration")]` tests would hang indefinitely (Testcontainers cannot start the Postgres container). Per task instructions, `dotnet test` was deliberately not run. Correctness was instead verified by:
  - Reading `GetDocumentsPagedAsync` in `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` line-by-line against every new test's assertions (sort switch, `Skip`/`Take` paging, `CountAsync` total).
  - Confirming the task context's assumed source code (sort switch, method signature, `MakeDocument` helper signature) matches the actual current files exactly — no adaptation was needed.
- No production code was touched; this is a test-only addition.
- The new tests are tagged `Category=Integration` via the class-level `[Trait]`, so they remain excluded from the standard CI filter (`Category!=Playwright&Category!=Integration`), same as the rest of the file.

## PR Summary

Adds 6 test methods (5 facts + 1 two-case theory) to the existing
`LeafletDocumentRepositoryPagedTests` integration test file, closing the
remaining coverage gap on `LeafletDocumentRepository.GetDocumentsPagedAsync`.
The new tests exercise all four branches of the sort-by switch statement
(`Filename`, `Status`, `IndexedAt` — including Postgres's NULLS FIRST/LAST
default placement for the nullable `IndexedAt` column — and the fallback to
`IngestedAt` for an empty or unrecognized `sortBy` value) in both ascending
and descending order, plus paging behavior: stable `Total` across multiple
pages of a 5-document set, and confirmation that `Total` reflects the
filtered row count rather than the returned page size. The build was
verified clean (0 errors) and `dotnet format --verify-no-changes` passes on
the changed file. These are Testcontainers-based Postgres integration tests
tagged `Category=Integration`, so they were not executed in this sandbox
(no Docker daemon available) — this is a known, pre-existing sandbox
limitation; correctness was verified by careful manual review of the
production sort/paging logic against each assertion.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs` — appended 6 new test methods covering sort-by-Filename, sort-by-Status, sort-by-IndexedAt (with nulls), unrecognized-sortBy fallback, page-slicing stability, and filtered-vs-paged total count.

## Status
DONE_WITH_CONCERNS
