### task: add-paged-sort-and-paging-tests


#### Context (self-contained — restate, do not assume prior sections are visible)

You are appending 6 new test methods (4 `[Fact]`, plus 1 `[Theory]` and 1 more `[Fact]`) to an
**already-existing** file created by a prior task:
`backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs`

That file already contains (do not recreate — open and extend it):
- `[Trait("Category", "Integration")] public class LeafletDocumentRepositoryPagedTests : IAsyncLifetime`
  with a `PostgreSqlContainer` (`pgvector/pgvector:pg16`), `InitializeAsync`/`DisposeAsync`,
  `SetupSchemaAsync` (hand-rolled DDL for `"LeafletDocuments"`/`"LeafletChunks"`), and private
  fields `_context` (`ApplicationDbContext`) and `_repository` (`LeafletDocumentRepository`).
- A `MakeDocument` helper with this exact signature:
  ```csharp
  private static LeafletDocument MakeDocument(
      string filename = "test.pdf",
      string hash = "abc123",
      LeafletDocumentStatus status = LeafletDocumentStatus.Indexed,
      string contentType = "application/pdf",
      DateTime? indexedAt = null,
      DateTime? ingestedAt = null)
  ```
  Note: `indexedAt` defaults to `null` (not `DateTime.UtcNow`) — pass an explicit value whenever a
  test's assertions depend on `IndexedAt`.
- 6 existing test methods covering filters:
  `GetDocumentsPagedAsync_FilenameFilter_MatchesPartialCaseSensitive`,
  `GetDocumentsPagedAsync_FilenameFilter_EscapesLiteralWildcards`,
  `GetDocumentsPagedAsync_FilenameFilter_NoMatch_ReturnsEmptyPageAndZeroTotal`,
  `GetDocumentsPagedAsync_StatusFilter_MatchesEachEnumValue` (a `[Theory]`),
  `GetDocumentsPagedAsync_ContentTypeFilter_MatchesExactOnly`,
  `GetDocumentsPagedAsync_AllFiltersCombined_AndSemantics`.

The method under test
(`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`,
`GetDocumentsPagedAsync`) sorts like this:

```csharp
query = sortBy switch
{
    "Filename" => sortDescending
        ? query.OrderByDescending(d => d.Filename)
        : query.OrderBy(d => d.Filename),
    "Status" => sortDescending
        ? query.OrderByDescending(d => d.Status)
        : query.OrderBy(d => d.Status),
    "IndexedAt" => sortDescending
        ? query.OrderByDescending(d => d.IndexedAt)
        : query.OrderBy(d => d.IndexedAt),
    _ => sortDescending
        ? query.OrderByDescending(d => d.IngestedAt)
        : query.OrderBy(d => d.IngestedAt),
};

var total = await query.CountAsync(ct);
var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
return (items, total);
```

Key facts:
- `LeafletDocumentStatus` enum ordinals: `Processing = 0, Indexed = 1, Failed = 2` — sorting by
  `Status` sorts by this ordinal.
- Postgres's documented default `NULLS` placement (no explicit `NULLS FIRST/LAST` needed in the
  generated SQL to get this): ascending `ORDER BY` puts `NULL`s **last**; descending `ORDER BY`
  puts `NULL`s **first**. `IndexedAt` is the only nullable sort column here.
- Any `sortBy` value other than `"Filename"`, `"Status"`, or `"IndexedAt"` (including `""` and
  typos) falls through the `_ =>` arm to `IngestedAt` ordering.
- `Skip((pageNumber - 1) * pageSize).Take(pageSize)` — 1-based `pageNumber`.
- The return type is `(IReadOnlyList<LeafletDocument> Items, int Total)` — deconstruct with
  `var (items, total) = await _repository.GetDocumentsPagedAsync(...)`.
- Call `_repository.AddDocumentAsync(doc)` to seed each document (commits eagerly).

#### Step 1 — write the sort-by-Filename and sort-by-Status tests (FR-3.7, FR-3.8)

Open `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs`.
Insert the following two methods immediately before the class's final closing brace (i.e., after
`GetDocumentsPagedAsync_AllFiltersCombined_AndSemantics`):

```csharp
    [Fact]
    public async Task GetDocumentsPagedAsync_SortByFilename_BothDirections()
    {
        // Arrange
        var docA = MakeDocument("alpha.pdf", "leaflet-paged-hash-020");
        var docB = MakeDocument("bravo.pdf", "leaflet-paged-hash-021");
        var docC = MakeDocument("charlie.pdf", "leaflet-paged-hash-022");
        await _repository.AddDocumentAsync(docA);
        await _repository.AddDocumentAsync(docB);
        await _repository.AddDocumentAsync(docC);

        // Act
        var (ascItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);
        var (descItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);

        // Assert
        Assert.Equal(new[] { "alpha.pdf", "bravo.pdf", "charlie.pdf" }, ascItems.Select(d => d.Filename).ToArray());
        Assert.Equal(new[] { "charlie.pdf", "bravo.pdf", "alpha.pdf" }, descItems.Select(d => d.Filename).ToArray());
    }

    [Fact]
    public async Task GetDocumentsPagedAsync_SortByStatus_BothDirections()
    {
        // Arrange
        var docProcessing = MakeDocument("status-sort-processing.pdf", "leaflet-paged-hash-023", status: LeafletDocumentStatus.Processing);
        var docIndexed = MakeDocument("status-sort-indexed.pdf", "leaflet-paged-hash-024", status: LeafletDocumentStatus.Indexed);
        var docFailed = MakeDocument("status-sort-failed.pdf", "leaflet-paged-hash-025", status: LeafletDocumentStatus.Failed);
        await _repository.AddDocumentAsync(docProcessing);
        await _repository.AddDocumentAsync(docIndexed);
        await _repository.AddDocumentAsync(docFailed);

        // Act
        var (ascItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Status", sortDescending: false,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);
        var (descItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Status", sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);

        // Assert: enum ordinal order — Processing (0) < Indexed (1) < Failed (2).
        Assert.Equal(
            new[] { LeafletDocumentStatus.Processing, LeafletDocumentStatus.Indexed, LeafletDocumentStatus.Failed },
            ascItems.Select(d => d.Status).ToArray());
        Assert.Equal(
            new[] { LeafletDocumentStatus.Failed, LeafletDocumentStatus.Indexed, LeafletDocumentStatus.Processing },
            descItems.Select(d => d.Status).ToArray());
    }
```

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests&FullyQualifiedName~SortByFilename|FullyQualifiedName~SortByStatus"
```
Expected:
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```

#### Step 2 — write the sort-by-IndexedAt and default-sortBy-fallback tests (FR-3.9, FR-3.10)

Insert the following two methods right after the two from Step 1 (still before the class's final
closing brace):

```csharp
    [Fact]
    public async Task GetDocumentsPagedAsync_SortByIndexedAt_BothDirections_WithNulls()
    {
        // Arrange: two documents with distinct IndexedAt timestamps, one with IndexedAt = null.
        var now = DateTime.UtcNow;
        var docEarly = MakeDocument("indexed-early.pdf", "leaflet-paged-hash-026", indexedAt: now.AddHours(-2));
        var docLate = MakeDocument("indexed-late.pdf", "leaflet-paged-hash-027", indexedAt: now.AddHours(-1));
        var docNull = MakeDocument("indexed-null.pdf", "leaflet-paged-hash-028", indexedAt: null);
        await _repository.AddDocumentAsync(docEarly);
        await _repository.AddDocumentAsync(docLate);
        await _repository.AddDocumentAsync(docNull);

        // Act
        var (ascItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "IndexedAt", sortDescending: false,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);
        var (descItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "IndexedAt", sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);

        // Assert: Postgres default — NULLS LAST for ascending, NULLS FIRST for descending.
        Assert.Equal(
            new[] { "indexed-early.pdf", "indexed-late.pdf", "indexed-null.pdf" },
            ascItems.Select(d => d.Filename).ToArray());
        Assert.Equal(
            new[] { "indexed-null.pdf", "indexed-late.pdf", "indexed-early.pdf" },
            descItems.Select(d => d.Filename).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotARealColumn")]
    public async Task GetDocumentsPagedAsync_UnrecognizedSortBy_FallsBackToIngestedAt(string sortBy)
    {
        // Arrange: two documents with distinct IngestedAt timestamps.
        var now = DateTime.UtcNow;
        var docOld = MakeDocument("ingested-old.pdf", "leaflet-paged-hash-029", ingestedAt: now.AddHours(-2));
        var docNew = MakeDocument("ingested-new.pdf", "leaflet-paged-hash-030", ingestedAt: now.AddHours(-1));
        await _repository.AddDocumentAsync(docOld);
        await _repository.AddDocumentAsync(docNew);

        // Act
        var (ascItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: sortBy, sortDescending: false,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);
        var (descItems, _) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: sortBy, sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);

        // Assert: the "_ =>" switch arm (IngestedAt ordering) is reached regardless of how the
        // caller misspells or omits sortBy.
        Assert.Equal(
            new[] { "ingested-old.pdf", "ingested-new.pdf" },
            ascItems.Select(d => d.Filename).ToArray());
        Assert.Equal(
            new[] { "ingested-new.pdf", "ingested-old.pdf" },
            descItems.Select(d => d.Filename).ToArray());
    }
```

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests&(FullyQualifiedName~SortByIndexedAt|FullyQualifiedName~UnrecognizedSortBy)"
```
Expected:
```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3
```
(3 = 1 `IndexedAt` test + 2 `[Theory]` cases of `UnrecognizedSortBy`.)

#### Step 3 — write the paging/total-count tests (FR-3.11, FR-3.12)

Insert the following two methods right after the tests from Step 2, still before the class's final
closing brace:

```csharp
    [Fact]
    public async Task GetDocumentsPagedAsync_PageSlicing_StableTotal()
    {
        // Arrange: 5 documents with distinct IngestedAt timestamps; docs[0] is most-recently
        // ingested, docs[4] least recently.
        var now = DateTime.UtcNow;
        var docs = Enumerable.Range(0, 5)
            .Select(i => MakeDocument($"page-doc-{i}.pdf", $"leaflet-paged-hash-{40 + i}", ingestedAt: now.AddMinutes(-i)))
            .ToList();
        foreach (var doc in docs)
            await _repository.AddDocumentAsync(doc);

        // Act: sortBy "" falls back to IngestedAt (the default column); sortDescending: true
        // means most-recently-ingested first, matching "page 1 = the 2 most-recently-ingested".
        var (page1, total1) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 2, sortBy: "", sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);
        var (page2, total2) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 2, pageSize: 2, sortBy: "", sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);
        var (page3, total3) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 3, pageSize: 2, sortBy: "", sortDescending: true,
            filenameFilter: null, statusFilter: null, contentTypeFilter: null);

        // Assert: Total is stable across all pages; each page returns the correct slice.
        Assert.Equal(5, total1);
        Assert.Equal(5, total2);
        Assert.Equal(5, total3);
        Assert.Equal(new[] { "page-doc-0.pdf", "page-doc-1.pdf" }, page1.Select(d => d.Filename).ToArray());
        Assert.Equal(new[] { "page-doc-2.pdf", "page-doc-3.pdf" }, page2.Select(d => d.Filename).ToArray());
        Assert.Equal(new[] { "page-doc-4.pdf" }, page3.Select(d => d.Filename).ToArray());
    }

    [Fact]
    public async Task GetDocumentsPagedAsync_Total_ReflectsFilteredCount_NotPagedCount()
    {
        // Arrange: 3 of 5 documents match the contentType filter.
        var docMatch1 = MakeDocument("filtered-match-1.pdf", "leaflet-paged-hash-050", contentType: "application/pdf");
        var docMatch2 = MakeDocument("filtered-match-2.pdf", "leaflet-paged-hash-051", contentType: "application/pdf");
        var docMatch3 = MakeDocument("filtered-match-3.pdf", "leaflet-paged-hash-052", contentType: "application/pdf");
        var docNoMatch1 = MakeDocument("filtered-nomatch-1.pdf", "leaflet-paged-hash-053", contentType: "image/png");
        var docNoMatch2 = MakeDocument("filtered-nomatch-2.pdf", "leaflet-paged-hash-054", contentType: "image/png");
        await _repository.AddDocumentAsync(docMatch1);
        await _repository.AddDocumentAsync(docMatch2);
        await _repository.AddDocumentAsync(docMatch3);
        await _repository.AddDocumentAsync(docNoMatch1);
        await _repository.AddDocumentAsync(docNoMatch2);

        // Act: pageSize smaller than the filtered match count.
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 2, sortBy: "Filename", sortDescending: false,
            filenameFilter: null, statusFilter: null, contentTypeFilter: "application/pdf");

        // Assert: Total reflects the filtered count (3), not the returned page size (2).
        Assert.Equal(2, items.Count);
        Assert.Equal(3, total);
    }
```

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests&(FullyQualifiedName~PageSlicing|FullyQualifiedName~ReflectsFilteredCount)"
```
Expected:
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```

#### Step 4 — run the entire new file, then build/format-check the whole solution

```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests"
```
Expected:
```
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15
```
(15 = 8 filter-test cases from the prior task + 7 new sort/paging test cases from this task
[`SortByFilename` (1) + `SortByStatus` (1) + `SortByIndexedAt` (1) + `UnrecognizedSortBy` theory
(2 cases) + `PageSlicing` (1) + `ReflectsFilteredCount` (1) = 7].)

Then, for the whole repository:
```
cd backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln --filter "Category=Integration&FullyQualifiedName~Leaflet"
```
Expected: build succeeds with 0 errors; format check passes; the final Integration-filtered run
shows all Leaflet integration tests passing (19 from
`LeafletRepositoryIntegrationTests` + 15 from `LeafletDocumentRepositoryPagedTests` = 34 total),
e.g.:
```
Passed!  - Failed:     0, Passed:    34, Skipped:     0, Total:    34
```

Also confirm the standard (non-Integration) CI filter still passes unaffected, since these new
tests are correctly excluded from it:
```
cd backend
dotnet test Anela.Heblo.sln --filter "Category!=Playwright&Category!=Integration"
```
Expected: build/test succeeds as it did before this change (no Leaflet Integration tests appear in
the list; this validates the `[Trait("Category","Integration")]` tag is correctly excluding them).

#### Step 5 — commit

```
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs
git commit -m "Add GetDocumentsPagedAsync sort and paging tests

Completes coverage of the four sort branches (Filename, Status, IndexedAt
with Postgres's default NULLS placement, and the unrecognized/empty
sortBy fallback to IngestedAt) in both directions, plus page-slicing
stability and the filtered-vs-paged Total distinction. Test-only change;
Category=Integration, excluded from CI coverage runs per existing
workflow filters."
```

---

## Self-review notes (performed against spec.r1.md; issues found were fixed inline above)

- **FR coverage**: every FR-1.x, FR-2.x, and FR-3.x sub-requirement in spec.r1.md maps to exactly
  one test method in exactly one task (see the traceability matrix above). No FR is orphaned, and
  none is duplicated across tasks.
- **Acceptance-criteria items with "no new test needed"** (the `AddChunksAsync` connection-state
  guard, and `SearchSimilarAsync`'s `CommandTimeout = 120` literal) are explicitly called out as
  informational notes inside task 1, matching the spec's own instruction not to add dedicated
  tests for them — this avoids an engineer misreading silence as an oversight.
- **Placeholder scan**: no "TBD", "add appropriate assertions", or "similar to above" language
  appears anywhere in the task bodies — every test method above is complete, real C#, copy-pasteable
  as written, with concrete expected values and concrete `dotnet test --filter` invocations.
- **Type/name consistency check**: `LeafletDocumentStatus` (`Processing/Indexed/Failed`),
  `LeafletDocument`/`LeafletChunk` property names, `ILeafletDocumentRepository.GetDocumentsPagedAsync`'s
  parameter list and order, and the `(Items, Total)` / `(Chunk, Score)` tuple names were all taken
  directly from the current source files (not assumed) and are used identically across all three
  tasks.
- **Ambiguity resolved during planning, documented explicitly**: the spec's FR-3.11 says page 1
  should contain "the 2 most-recently-ingested" documents but does not state the `sortDescending`
  value to pass alongside the default `sortBy`. Since `GetDocumentsPagedAsync` sorts ascending
  by default only when `sortDescending: false` is passed, and "most-recently-ingested first"
  requires descending order, `add-paged-sort-and-paging-tests` Step 3 explicitly passes
  `sortDescending: true` and documents this choice inline in a code comment, rather than leaving
  the direction implicit or guessing silently.
- **New helper's `indexedAt` default changed from the sibling file's convention** (`null` instead
  of `DateTime.UtcNow`) because FR-3.9 requires a document with a genuinely null `IndexedAt`, and a
  `?? DateTime.UtcNow` fallback would make that impossible to express through the helper. This
  divergence from the existing `MakeDocument` is called out explicitly in task 2's helper comment
  so a reviewer doesn't mistake it for a copy-paste error.
