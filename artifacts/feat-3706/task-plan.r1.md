# Task Plan: Close coverage gap in `LeafletDocumentRepository`

## Feature
Add missing integration-test coverage for three un-exercised code paths in
`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`:
the `AddChunksAsync` multi-batch loop, the `SearchSimilarAsync` raw-SQL reader
mapping, and the `GetDocumentsPagedAsync` filter/sort/paging logic (currently
zero repository-level tests).

## Goal
By the end of this plan:
- `AddChunksAsync` is verified across the `MaxRowsPerBatch = 1000` batch
  boundary (exactly 1000 chunks, and 1001 chunks spanning two `INSERT`s).
- `SearchSimilarAsync` is verified for every reader-mapped column (ordinals
  0–8), the cross-document `JOIN` correctness, the intentionally-empty
  `Embedding`, `topK` limiting, and the empty-result path.
- `GetDocumentsPagedAsync` — currently untested against the real repository —
  gets full coverage of its three filters (individually and combined), its
  four sort branches (`Filename`, `Status`, `IndexedAt`, default/unrecognized)
  in both directions, and its paging/total-count behavior.
- This is a **test-only** change. No file under `backend/src/` is modified.
  No `ILeafletDocumentRepository` contract change. No new NuGet packages.

## Architecture summary
`LeafletDocumentRepository` is Persistence-layer infrastructure
(`Anela.Heblo.Persistence.Features.Leaflet` namespace) implementing
`ILeafletDocumentRepository` (`Anela.Heblo.Domain.Features.Leaflet`). It mixes
EF Core (`ApplicationDbContext`) for simple CRUD with raw `NpgsqlCommand`/
`NpgsqlDataReader` for two methods that need Npgsql-specific features not
expressible in LINQ-to-Entities: `AddChunksAsync` (multi-row `INSERT ... ON
CONFLICT DO NOTHING` built with a `StringBuilder` and per-row `@name{i}`
parameters, batched at `MaxRowsPerBatch = 1000` rows) and `SearchSimilarAsync`
(cosine-distance `<=>` operator over a `pgvector` `vector(3)` column via
`Pgvector.Vector`). `GetDocumentsPagedAsync` is pure LINQ-to-Entities
(`IQueryable<LeafletDocument>` with conditional `.Where`/`.OrderBy` and
`EF.Functions.Like` with an escape character) but is only ever exercised today
through handler tests that mock `ILeafletDocumentRepository` entirely
(`GetLeafletDocumentsHandlerTests.cs`), so its real SQL translation has never
run.

Because of the Npgsql-specific `Vector` type, the `<=>` operator, and the
3-argument `EF.Functions.Like` overload, none of this is testable against
InMemory/SQLite — the existing test file already solves this with
`Testcontainers.PostgreSql` (`pgvector/pgvector:pg16` image) and a
hand-rolled DDL script (`SetupSchemaAsync`) that mirrors
`LeafletDocumentConfiguration`/`LeafletChunkConfiguration`. All new tests
reuse this exact pattern — no new test infrastructure is introduced.

**Important CI fact carried over from the spec:** both
`.github/workflows/ci-feature-branch.yml` (lines 87, 93) and
`ci-main-branch.yml` (line 150) run `dotnet test` with
`--filter "Category!=Playwright&Category!=Integration"`. No workflow anywhere
runs `Category=Integration`. This means the tests added here will verify real
behavior against real Postgres/pgvector semantics, but will **not** move the
CI-gated line-coverage percentage. That is a pre-existing gap in the CI setup,
explicitly out of scope for this plan (per spec.r1.md "Out of Scope") — do not
attempt to fix it as part of this work.

## Tech stack
- .NET 8, C# (nullable enabled, implicit usings enabled — `System.Linq` is
  available without an explicit `using`).
- xUnit 2.9.2 / `xunit.runner.visualstudio` 2.8.2, plain `Assert.*` style
  (this file does not use FluentAssertions even though it is referenced in
  the test project and preferred by `docs/architecture/testing-strategy.md` —
  match the file's actual existing convention, not the doc).
- `Testcontainers.PostgreSql` 3.6.0, image `pgvector/pgvector:pg16`.
- `Npgsql` + `Pgvector` (`.UseVector()` on `NpgsqlDataSourceBuilder`).
- Solution file: `Anela.Heblo.sln` (repo root). Test project:
  `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.
- All new/changed tests are tagged `[Trait("Category", "Integration")]` at
  the class level and require a container runtime (Docker or Podman)
  available on the machine running them. Docker was confirmed available in
  this environment (`docker version` succeeds).

## Files touched by this plan
- **Extend**: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`
  (currently 445 lines, 14 tests) — task 1.
- **Create**: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs`
  — tasks 2 and 3 (task 2 creates it with filter tests; task 3 appends sort/
  paging tests to it).
- **No production code files are touched.**

## FR → task traceability matrix
| Requirement | Task |
|---|---|
| FR-1.1 (exact batch boundary, 1000 chunks) | `add-batch-boundary-and-reader-mapping-tests` |
| FR-1.2 (two-batch path, 1001 chunks) | `add-batch-boundary-and-reader-mapping-tests` |
| FR-2.1 (full reader-column mapping, cross-document JOIN, empty Embedding) | `add-batch-boundary-and-reader-mapping-tests` |
| FR-2.2 (`topK` limits result count) | `add-batch-boundary-and-reader-mapping-tests` |
| FR-2.3 (empty result set, no exception) | `add-batch-boundary-and-reader-mapping-tests` |
| FR-3.1 (`filenameFilter` partial, case-sensitive) | `add-paged-filter-tests` |
| FR-3.2 (`filenameFilter` escapes `%`/`_`) | `add-paged-filter-tests` |
| FR-3.3 (`filenameFilter` no match → empty page, `Total==0`) | `add-paged-filter-tests` |
| FR-3.4 (`statusFilter`, each enum value) | `add-paged-filter-tests` |
| FR-3.5 (`contentTypeFilter`, exact match only) | `add-paged-filter-tests` |
| FR-3.6 (all three filters combined, AND semantics) | `add-paged-filter-tests` |
| FR-3.7 (sort by `Filename`, both directions) | `add-paged-sort-and-paging-tests` |
| FR-3.8 (sort by `Status`, both directions, enum ordinal) | `add-paged-sort-and-paging-tests` |
| FR-3.9 (sort by `IndexedAt`, both directions, `NULLS` default) | `add-paged-sort-and-paging-tests` |
| FR-3.10 (unrecognized/empty `sortBy` falls back to `IngestedAt`) | `add-paged-sort-and-paging-tests` |
| FR-3.11 (page slicing + stable `Total`) | `add-paged-sort-and-paging-tests` |
| FR-3.12 (`Total` reflects filtered count, not paged count) | `add-paged-sort-and-paging-tests` |

Note on FR-1's connection-state-guard acceptance criterion and FR-2's
`CommandTimeout = 120` acceptance criterion: both are explicitly **no new
test required** per the spec (already structurally covered by existing/new
calls). They are called out here for traceability but need no dedicated task
step — see the notes inside task 1 below.

Task decomposition note: the spec/design suggested a 2-file split (existing
file vs. one new sibling file). Reading the actual code, FR-3 alone spans 12
sub-requirements (~13 test methods, two of them `[Theory]`s) plus new
class/container/schema/helper scaffolding — too much for one self-contained,
independently-reviewable task. This plan therefore uses **3 tasks**: task 1
covers the existing file (FR-1 + FR-2, 5 test methods), task 2 creates the new
file with its scaffolding and the 3 filter sub-cases (FR-3.1–3.6, 6 test
methods), and task 3 appends the sort/paging sub-cases (FR-3.7–3.12, 6 test
methods) to the file task 2 created. Tasks 2 and 3 must run in that order
(task 3 edits the file task 2 creates); task 1 is independent of both and can
run in any order relative to them.

---

### task: add-batch-boundary-and-reader-mapping-tests

#### Context (self-contained — restate, do not assume prior sections are visible)

You are adding 5 new `[Fact]` test methods to an **existing** file:
`backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`

This file already has this exact shape (do not change any of the following —
reuse it as-is):

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Integration;

[Trait("Category", "Integration")]
public class LeafletRepositoryIntegrationTests : IAsyncLifetime
{
    static LeafletRepositoryIntegrationTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private ApplicationDbContext _context = null!;
    private LeafletDocumentRepository _repository = null!;

    public async Task InitializeAsync() { /* starts container, builds ApplicationDbContext via
        NpgsqlDataSourceBuilder(...).UseVector(), calls SetupSchemaAsync(), constructs _repository */ }
    public async Task DisposeAsync() { /* disposes _context and _container */ }
    private async Task SetupSchemaAsync() { /* hand-rolled DDL for "LeafletDocuments" + "LeafletChunks"
        tables + HNSW index on a vector(3) "Embedding" column — do not change */ }

    private static LeafletDocument MakeDocument(
        string filename = "test.pdf",
        string hash = "abc123",
        string? driveId = null,
        string? graphItemId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            SourcePath = $"/leaflets/{filename}",
            ContentType = "application/pdf",
            ContentHash = hash,
            Status = LeafletDocumentStatus.Indexed,
            IngestedAt = DateTime.UtcNow,
            WordCount = 100,
            DriveId = driveId,
            GraphItemId = graphItemId,
            IndexedAt = DateTime.UtcNow
        };

    // ... 14 existing [Fact] tests, including:
    //   AddChunksAsync_PersistsSummary
    //   AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId
    //   AddChunksAsync_PersistsAllRows_WhenMultipleChunks   <-- ends the AddChunksAsync_* block
    //   AddChunksAsync_IsNoOp_WhenInputEmpty
    //   AddDocumentAndChunks_CanBeRetrievedByHash
    //   SearchSimilarAsync_ReturnsClosestChunkByCosineSimilarity
    //   SearchSimilarAsync_ReturnsChunkWithSummary            <-- ends the SearchSimilarAsync_* block
    //   GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists
    //   ... (DeleteDocumentAsync_CascadesToChunks, GetChunkByIdAsync_ReturnsNull_WhenNotExists,
    //        GetByGraphItemIdAsync_* x3)
}
```

The domain types you'll use (`backend/src/Anela.Heblo.Domain/Features/Leaflet/`):

```csharp
// LeafletDocument.cs
public class LeafletDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public int WordCount { get; set; }
    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }
    public LeafletDocumentStatus Status { get; set; } = LeafletDocumentStatus.Processing;
    public DateTime? IndexedAt { get; set; }
    public ICollection<LeafletChunk> Chunks { get; set; } = new List<LeafletChunk>();
}

// LeafletChunk.cs
public class LeafletChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public LeafletDocument Document { get; set; } = null!;
}
```

The repository methods under test
(`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`):

- `public async Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)` —
  loops `for (var offset = 0; offset < chunkList.Count; offset += MaxRowsPerBatch)` where
  `MaxRowsPerBatch = 1000`, building one multi-row raw `INSERT ... ON CONFLICT ("Id") DO NOTHING`
  per batch, each with its own fresh `NpgsqlCommand` and its own `@id0..`-style parameter set.
- `public async Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(float[] queryEmbedding, int topK, CancellationToken ct = default)` —
  raw SQL: `SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content", c."Summary", c."WordCount", d."Filename", d."SourcePath", 1 - (c."Embedding" <=> @embedding) AS "Score" FROM "LeafletChunks" c JOIN "LeafletDocuments" d ON d."Id" = c."DocumentId" ORDER BY c."Embedding" <=> @embedding LIMIT @topK`,
  with `CommandTimeout = 120`. The reader always sets `chunk.Embedding = []` (never reads an
  embedding column back) — this is intentional, existing behavior.

`_repository.AddDocumentAsync(doc)` commits eagerly (`SaveChangesAsync` inside the method), so it
is safe to call before `AddChunksAsync` to satisfy the FK.

#### Step 1 — write the two `AddChunksAsync` batch-boundary tests (FR-1.1, FR-1.2)

Open `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`.
Find the existing method `AddChunksAsync_IsNoOp_WhenInputEmpty` (it ends right before
`AddDocumentAndChunks_CanBeRetrievedByHash`). Insert the following two methods immediately after
`AddChunksAsync_IsNoOp_WhenInputEmpty`'s closing brace, before `AddDocumentAndChunks_CanBeRetrievedByHash`:

```csharp
    [Fact]
    public async Task AddChunksAsync_PersistsAll_AtExactBatchBoundary()
    {
        // Arrange: exactly MaxRowsPerBatch (1000) chunks — a single batch, boundary edge.
        var doc = MakeDocument("batch-boundary-test.pdf", "leaflet-hash-100");
        await _repository.AddDocumentAsync(doc);

        var chunks = Enumerable.Range(0, 1000)
            .Select(i => new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                ChunkIndex = i,
                Content = $"Boundary content {i}",
                Summary = $"Boundary summary {i}",
                WordCount = i + 1,
                Embedding = [(float)i, 0f, 0f]
            })
            .ToList();

        // Act
        await _repository.AddChunksAsync(chunks);

        // Assert: all 1000 rows persisted, first and last of the single batch round-trip correctly.
        var stored = await _context.LeafletChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == doc.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(1000, stored.Count);

        var first = stored[0];
        Assert.Equal(0, first.ChunkIndex);
        Assert.Equal("Boundary content 0", first.Content);
        Assert.Equal("Boundary summary 0", first.Summary);
        Assert.Equal(1, first.WordCount);

        var last = stored[999];
        Assert.Equal(999, last.ChunkIndex);
        Assert.Equal("Boundary content 999", last.Content);
        Assert.Equal("Boundary summary 999", last.Summary);
        Assert.Equal(1000, last.WordCount);
    }

    [Fact]
    public async Task AddChunksAsync_PersistsAll_AcrossTwoBatches()
    {
        // Arrange: MaxRowsPerBatch (1000) + 1 = 1001 chunks — forces a second INSERT with a
        // fresh NpgsqlCommand and its own @id0-style parameter set (proves no parameter-name collision).
        var doc = MakeDocument("two-batch-test.pdf", "leaflet-hash-101");
        await _repository.AddDocumentAsync(doc);

        var chunks = Enumerable.Range(0, 1001)
            .Select(i => new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                ChunkIndex = i,
                Content = $"TwoBatch content {i}",
                Summary = $"TwoBatch summary {i}",
                WordCount = i + 1,
                Embedding = [(float)i, 0f, 0f]
            })
            .ToList();

        // Act: single call internally issues two INSERTs (batch 1: ChunkIndex 0..999, batch 2: 1000).
        await _repository.AddChunksAsync(chunks);

        // Assert: no row silently dropped or duplicated across the boundary.
        var stored = await _context.LeafletChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == doc.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(1001, stored.Count);

        // Last row of batch 1.
        var lastOfBatch1 = stored[999];
        Assert.Equal(999, lastOfBatch1.ChunkIndex);
        Assert.Equal("TwoBatch content 999", lastOfBatch1.Content);
        Assert.Equal("TwoBatch summary 999", lastOfBatch1.Summary);
        Assert.Equal(1000, lastOfBatch1.WordCount);

        // Sole row of batch 2 — proves the second NpgsqlCommand's own parameter set executed
        // correctly and did not collide with or overwrite batch 1's parameters.
        var onlyRowOfBatch2 = stored[1000];
        Assert.Equal(1000, onlyRowOfBatch2.ChunkIndex);
        Assert.Equal("TwoBatch content 1000", onlyRowOfBatch2.Content);
        Assert.Equal("TwoBatch summary 1000", onlyRowOfBatch2.Summary);
        Assert.Equal(1001, onlyRowOfBatch2.WordCount);
    }
```

Note (do not act on this — informational only, satisfies FR-1's acceptance criterion): the
connection-state guard (`if (connection.State != System.Data.ConnectionState.Open)`) is already
exercised by the existing `AddChunksAsync_PersistsSummary` test (closed → open branch, first call
in a fresh container) and `AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId` (its second call
hits the already-open branch). No dedicated test for this guard is added or needed here.

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests&FullyQualifiedName~AddChunksAsync_PersistsAll"
```
Expected: this should **fail to compile or fail to run** only if you made a typo; assuming the
code above is pasted correctly, expect:
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```
(Each test starts its own Postgres container, so this may take ~5-15 seconds per test.)

#### Step 2 — write the three `SearchSimilarAsync` reader-mapping tests (FR-2.1, FR-2.2, FR-2.3)

Find the existing method `SearchSimilarAsync_ReturnsChunkWithSummary` (it ends right before
`GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists`). Insert the following three methods
immediately after `SearchSimilarAsync_ReturnsChunkWithSummary`'s closing brace, before
`GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists`:

```csharp
    [Fact]
    public async Task SearchSimilarAsync_MapsAllReaderColumns_AcrossTwoDocuments()
    {
        // Arrange: two distinct documents, one chunk each, orthogonal embeddings so similarity
        // ordering is unambiguous.
        var doc1 = MakeDocument("mapping-doc-one.pdf", "leaflet-hash-102");
        var doc2 = MakeDocument("mapping-doc-two.pdf", "leaflet-hash-103");
        await _repository.AddDocumentAsync(doc1);
        await _repository.AddDocumentAsync(doc2);

        var chunk1 = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc1.Id,
            ChunkIndex = 7,
            Content = "Doc one content",
            Summary = "Doc one summary",
            WordCount = 11,
            Embedding = [1.0f, 0.0f, 0.0f]
        };
        var chunk2 = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc2.Id,
            ChunkIndex = 3,
            Content = "Doc two content",
            Summary = "Doc two summary",
            WordCount = 13,
            Embedding = [0.0f, 1.0f, 0.0f]
        };
        await _repository.AddChunksAsync([chunk1, chunk2]);

        // Act: query aligned with chunk1 => chunk1 must be the top (closest) result.
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 2);

        // Assert
        Assert.Equal(2, results.Count);
        var top = results[0];

        Assert.Equal(chunk1.Id, top.Chunk.Id);
        Assert.Equal(doc1.Id, top.Chunk.DocumentId);
        Assert.Equal(7, top.Chunk.ChunkIndex);
        Assert.Equal("Doc one content", top.Chunk.Content);
        Assert.Equal("Doc one summary", top.Chunk.Summary);
        Assert.Equal(11, top.Chunk.WordCount);

        // Proves the JOIN on ordinals 6/7 resolves to the correct (own) document per row,
        // not a fixed/first document.
        Assert.Equal(doc1.Id, top.Chunk.Document.Id);
        Assert.Equal("mapping-doc-one.pdf", top.Chunk.Document.Filename);
        Assert.Equal("/leaflets/mapping-doc-one.pdf", top.Chunk.Document.SourcePath);

        // Intentional current behavior: the reader never populates Embedding.
        Assert.Empty(top.Chunk.Embedding);

        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task SearchSimilarAsync_TopKLimitsResultCount()
    {
        // Arrange: 3 chunks with distinct embeddings.
        var doc = MakeDocument("topk-test.pdf", "leaflet-hash-104");
        await _repository.AddDocumentAsync(doc);

        var chunks = new[]
        {
            new LeafletChunk { Id = Guid.NewGuid(), DocumentId = doc.Id, ChunkIndex = 0, Content = "A", Summary = "A", WordCount = 1, Embedding = [1.0f, 0.0f, 0.0f] },
            new LeafletChunk { Id = Guid.NewGuid(), DocumentId = doc.Id, ChunkIndex = 1, Content = "B", Summary = "B", WordCount = 1, Embedding = [0.0f, 1.0f, 0.0f] },
            new LeafletChunk { Id = Guid.NewGuid(), DocumentId = doc.Id, ChunkIndex = 2, Content = "C", Summary = "C", WordCount = 1, Embedding = [0.0f, 0.0f, 1.0f] },
        };
        await _repository.AddChunksAsync(chunks);

        // Act
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 1);

        // Assert: LIMIT @topK is actually wired, not just present in the SQL text.
        Assert.Single(results);
    }

    [Fact]
    public async Task SearchSimilarAsync_ReturnsEmptyList_WhenNoChunks()
    {
        // Arrange: a document with zero chunks.
        var doc = MakeDocument("no-chunks-test.pdf", "leaflet-hash-105");
        await _repository.AddDocumentAsync(doc);

        // Act
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 5);

        // Assert: zero-iteration reader loop returns an empty, non-null list, no exception.
        Assert.NotNull(results);
        Assert.Empty(results);
    }
```

Note (informational only, satisfies FR-2's acceptance criterion): `CommandTimeout = 120` is a
fixed literal, structurally exercised by every call to `SearchSimilarAsync` above (satisfying line
coverage). No dedicated timeout-triggering test is added — that would require actually waiting out
or mocking a 120-second timeout, which is not a reasonable use of test time.

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests&FullyQualifiedName~SearchSimilarAsync"
```
Expected:
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```
(5 = the 2 existing `SearchSimilarAsync_*` tests plus the 3 new ones.)

#### Step 3 — run the full extended file and confirm nothing regressed

```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests"
```
Expected:
```
Passed!  - Failed:     0, Passed:    19, Skipped:     0, Total:    19
```
(19 = 14 existing + 5 new: 2 batch-boundary + 3 reader-mapping.)

Also run the non-Integration suite to confirm this change did not affect anything else (this file
is Integration-tagged, so it should not appear, but this validates the build overall):
```
cd backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```
Expected: build succeeds with 0 errors; `dotnet format --verify-no-changes` exits 0 (no formatting
diffs). If `dotnet format` reports diffs, run `dotnet format Anela.Heblo.sln` (without
`--verify-no-changes`) to apply them, then re-run the verify command.

#### Step 4 — commit

```
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs
git commit -m "Add AddChunksAsync batch-boundary and SearchSimilarAsync reader-mapping tests

Closes the coverage gap on LeafletDocumentRepository's multi-batch INSERT
loop (exact 1000-row boundary and the 1001-row two-batch path) and on the
raw-SQL reader mapping in SearchSimilarAsync (all 9 reader ordinals, the
cross-document JOIN, the intentional empty Embedding, topK limiting, and
the empty-result path). Test-only change; Category=Integration, excluded
from CI coverage runs per existing workflow filters."
```

---

### task: add-paged-filter-tests

#### Context (self-contained — restate, do not assume prior sections are visible)

You are creating a **new** file:
`backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs`

This covers `GetDocumentsPagedAsync` — a method with **zero** existing tests against the real
repository (it is only ever exercised via mocks in `GetLeafletDocumentsHandlerTests.cs`). This
task covers the three filters (FR-3.1–FR-3.6); a later task (`add-paged-sort-and-paging-tests`)
appends sort/paging tests to this same file.

The method under test
(`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`):

```csharp
public async Task<(IReadOnlyList<LeafletDocument> Items, int Total)> GetDocumentsPagedAsync(
    int pageNumber, int pageSize, string sortBy, bool sortDescending,
    string? filenameFilter, LeafletDocumentStatus? statusFilter, string? contentTypeFilter,
    CancellationToken ct = default)
{
    var query = _context.LeafletDocuments.AsNoTracking().AsQueryable();

    if (!string.IsNullOrEmpty(filenameFilter))
    {
        var escaped = filenameFilter.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        query = query.Where(d => EF.Functions.Like(d.Filename, $"%{escaped}%", "\\"));
    }

    if (statusFilter.HasValue)
        query = query.Where(d => d.Status == statusFilter.Value);

    if (!string.IsNullOrEmpty(contentTypeFilter))
        query = query.Where(d => d.ContentType == contentTypeFilter);

    query = sortBy switch { /* "Filename" | "Status" | "IndexedAt" | _ => IngestedAt, both directions */ };

    var total = await query.CountAsync(ct);
    var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    return (items, total);
}
```

Key facts you need:
- `filenameFilter` uses `EF.Functions.Like` with `\` as the escape char — `%` and `_` in user
  input are escaped to `\%`/`\_` (and a literal `\` is escaped to `\\` first) so they're matched
  literally, not as SQL wildcards. Postgres `LIKE` is case-sensitive under the default (`C`)
  collation used by the `pgvector/pgvector:pg16` test image — a lowercase pattern will **not**
  match a differently-cased filename.
- `contentTypeFilter` is an **exact** equality match, not partial.
- All three filters combine with **AND** semantics (each adds its own `.Where`).
- `LeafletDocumentStatus` enum: `Processing = 0, Indexed = 1, Failed = 2`
  (`backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletDocumentStatus.cs`).
- `LeafletDocument.IndexedAt` is `DateTime?` (nullable); `IngestedAt` is `DateTime` (non-nullable).

The interface (`backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs`)
is unchanged and must remain unchanged — this is a test-only task.

The **existing sibling file**
`backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`
establishes the pattern you must copy (do not invent a different one):
`[Trait("Category", "Integration")]` at class level, `IAsyncLifetime`, a `PostgreSqlContainer`
built with `.WithImage("pgvector/pgvector:pg16")`, a static constructor setting
`TestcontainersSettings.ResourceReaperEnabled = false` (Podman compatibility, no Ryuk), a
hand-rolled `SetupSchemaAsync` DDL script, and a `MakeDocument`-style factory helper with named
optional parameters.

#### Step 1 — create the file with full scaffolding + the 6 filter test methods

Create `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs`
with this exact content:

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Integration;

[Trait("Category", "Integration")]
public class LeafletDocumentRepositoryPagedTests : IAsyncLifetime
{
    static LeafletDocumentRepositoryPagedTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private ApplicationDbContext _context = null!;
    private LeafletDocumentRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        _context = new ApplicationDbContext(options);

        await SetupSchemaAsync();
        _repository = new LeafletDocumentRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS public."LeafletDocuments" (
                "Id"          uuid NOT NULL PRIMARY KEY,
                "Filename"    text NOT NULL,
                "SourcePath"  text NOT NULL,
                "ContentType" text NOT NULL,
                "ContentHash" varchar(64) NOT NULL,
                "IngestedAt"  timestamp NOT NULL,
                "WordCount"   integer NOT NULL,
                "DriveId"     text NULL,
                "GraphItemId" text NULL,
                "Status"      varchar(16) NOT NULL DEFAULT 'processing',
                "IndexedAt"   timestamp NULL
            );

            CREATE TABLE IF NOT EXISTS public."LeafletChunks" (
                "Id"          uuid NOT NULL PRIMARY KEY,
                "DocumentId"  uuid NOT NULL REFERENCES public."LeafletDocuments"("Id") ON DELETE CASCADE,
                "ChunkIndex"  integer NOT NULL,
                "Content"     text NOT NULL DEFAULT '',
                "Summary"     text NOT NULL DEFAULT '',
                "WordCount"   integer NOT NULL,
                "Embedding"   vector(3)
            );

            CREATE INDEX IF NOT EXISTS idx_leaflet_chunks_embedding
                ON public."LeafletChunks"
                USING hnsw ("Embedding" vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // Unlike the sibling file's MakeDocument (which only varies filename/hash/driveId/graphItemId),
    // this helper also varies status/contentType/indexedAt/ingestedAt, since GetDocumentsPagedAsync's
    // filter and sort tests need to control all of these. indexedAt defaults to null (not
    // DateTime.UtcNow) specifically so tests can distinguish "not specified" from "explicitly set" —
    // pass an explicit value whenever a test's assertions depend on IndexedAt.
    private static LeafletDocument MakeDocument(
        string filename = "test.pdf",
        string hash = "abc123",
        LeafletDocumentStatus status = LeafletDocumentStatus.Indexed,
        string contentType = "application/pdf",
        DateTime? indexedAt = null,
        DateTime? ingestedAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            SourcePath = $"/leaflets/{filename}",
            ContentType = contentType,
            ContentHash = hash,
            Status = status,
            IngestedAt = ingestedAt ?? DateTime.UtcNow,
            WordCount = 100,
            IndexedAt = indexedAt
        };

    [Fact]
    public async Task GetDocumentsPagedAsync_FilenameFilter_MatchesPartialCaseSensitive()
    {
        // Arrange
        var docA = MakeDocument("invoice-report.pdf", "leaflet-paged-hash-001");
        var docB = MakeDocument("Invoice-Summary.pdf", "leaflet-paged-hash-002");
        var docC = MakeDocument("other.pdf", "leaflet-paged-hash-003");
        await _repository.AddDocumentAsync(docA);
        await _repository.AddDocumentAsync(docB);
        await _repository.AddDocumentAsync(docC);

        // Act
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: "invoice", statusFilter: null, contentTypeFilter: null);

        // Assert: Postgres LIKE is case-sensitive under the default collation, so
        // "Invoice-Summary.pdf" (capital I) must NOT match a lowercase "invoice" pattern.
        Assert.Equal(1, total);
        var single = Assert.Single(items);
        Assert.Equal(docA.Id, single.Id);
        Assert.Equal("invoice-report.pdf", single.Filename);
    }

    [Fact]
    public async Task GetDocumentsPagedAsync_FilenameFilter_EscapesLiteralWildcards()
    {
        // Arrange: filter text itself contains SQL wildcard characters (% and _), which must be
        // treated literally, not as pattern wildcards.
        var docPercent = MakeDocument("50%_off.pdf", "leaflet-paged-hash-004");
        var docLiteralX = MakeDocument("50Xoff.pdf", "leaflet-paged-hash-005");
        await _repository.AddDocumentAsync(docPercent);
        await _repository.AddDocumentAsync(docLiteralX);

        // Act
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: "50%_off", statusFilter: null, contentTypeFilter: null);

        // Assert: only the literal match; "50Xoff.pdf" would incorrectly match if % / _ were
        // left as unescaped SQL wildcards.
        Assert.Equal(1, total);
        var single = Assert.Single(items);
        Assert.Equal(docPercent.Id, single.Id);
        Assert.Equal("50%_off.pdf", single.Filename);
    }

    [Fact]
    public async Task GetDocumentsPagedAsync_FilenameFilter_NoMatch_ReturnsEmptyPageAndZeroTotal()
    {
        // Arrange
        var doc = MakeDocument("something.pdf", "leaflet-paged-hash-006");
        await _repository.AddDocumentAsync(doc);

        // Act
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: "no-such-file", statusFilter: null, contentTypeFilter: null);

        // Assert
        Assert.Empty(items);
        Assert.Equal(0, total);
    }

    [Theory]
    [InlineData(LeafletDocumentStatus.Processing)]
    [InlineData(LeafletDocumentStatus.Indexed)]
    [InlineData(LeafletDocumentStatus.Failed)]
    public async Task GetDocumentsPagedAsync_StatusFilter_MatchesEachEnumValue(LeafletDocumentStatus status)
    {
        // Arrange: one document per status value.
        var docProcessing = MakeDocument("status-processing.pdf", "leaflet-paged-hash-007", status: LeafletDocumentStatus.Processing);
        var docIndexed = MakeDocument("status-indexed.pdf", "leaflet-paged-hash-008", status: LeafletDocumentStatus.Indexed);
        var docFailed = MakeDocument("status-failed.pdf", "leaflet-paged-hash-009", status: LeafletDocumentStatus.Failed);
        await _repository.AddDocumentAsync(docProcessing);
        await _repository.AddDocumentAsync(docIndexed);
        await _repository.AddDocumentAsync(docFailed);

        var expected = status switch
        {
            LeafletDocumentStatus.Processing => docProcessing,
            LeafletDocumentStatus.Indexed => docIndexed,
            LeafletDocumentStatus.Failed => docFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        // Act
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: null, statusFilter: status, contentTypeFilter: null);

        // Assert
        Assert.Equal(1, total);
        var single = Assert.Single(items);
        Assert.Equal(expected.Id, single.Id);
    }

    [Fact]
    public async Task GetDocumentsPagedAsync_ContentTypeFilter_MatchesExactOnly()
    {
        // Arrange
        var docPdf = MakeDocument("content-type-exact.pdf", "leaflet-paged-hash-010", contentType: "application/pdf");
        var docPdfX = MakeDocument("content-type-pdfx.pdf", "leaflet-paged-hash-011", contentType: "application/pdf-x");
        await _repository.AddDocumentAsync(docPdf);
        await _repository.AddDocumentAsync(docPdfX);

        // Act
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: null, statusFilter: null, contentTypeFilter: "application/pdf");

        // Assert: "application/pdf-x" must NOT match — this is exact equality, not partial.
        Assert.Equal(1, total);
        var single = Assert.Single(items);
        Assert.Equal(docPdf.Id, single.Id);
    }

    [Fact]
    public async Task GetDocumentsPagedAsync_AllFiltersCombined_AndSemantics()
    {
        // Arrange: only docMatch satisfies all three filters simultaneously.
        var docMatch = MakeDocument("invoice-final.pdf", "leaflet-paged-hash-012", status: LeafletDocumentStatus.Indexed, contentType: "application/pdf");
        var docWrongStatus = MakeDocument("invoice-draft.pdf", "leaflet-paged-hash-013", status: LeafletDocumentStatus.Processing, contentType: "application/pdf");
        var docWrongContentType = MakeDocument("invoice-scan.pdf", "leaflet-paged-hash-014", status: LeafletDocumentStatus.Indexed, contentType: "image/png");
        var docWrongName = MakeDocument("other-final.pdf", "leaflet-paged-hash-015", status: LeafletDocumentStatus.Indexed, contentType: "application/pdf");
        await _repository.AddDocumentAsync(docMatch);
        await _repository.AddDocumentAsync(docWrongStatus);
        await _repository.AddDocumentAsync(docWrongContentType);
        await _repository.AddDocumentAsync(docWrongName);

        // Act
        var (items, total) = await _repository.GetDocumentsPagedAsync(
            pageNumber: 1, pageSize: 10, sortBy: "Filename", sortDescending: false,
            filenameFilter: "invoice", statusFilter: LeafletDocumentStatus.Indexed, contentTypeFilter: "application/pdf");

        // Assert: exactly the one document matching filename AND status AND content type.
        Assert.Equal(1, total);
        var single = Assert.Single(items);
        Assert.Equal(docMatch.Id, single.Id);
    }
}
```

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletDocumentRepositoryPagedTests"
```
Expected:
```
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8
```
(8 = 5 `[Fact]` methods + 3 `[Theory]` cases from `GetDocumentsPagedAsync_StatusFilter_MatchesEachEnumValue`.)

If it fails on `GetDocumentsPagedAsync_FilenameFilter_MatchesPartialCaseSensitive` with
`"Invoice-Summary.pdf"` unexpectedly included, that means the `pgvector/pgvector:pg16` test image's
default collation is not case-sensitive in this environment — this is a genuine finding (per
arch-review.r1.md's documented risk), not a test bug. Do not silently loosen the assertion; report
it instead so the spec's assumption can be revisited.

Then build and format-check:
```
cd backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```
Expected: build succeeds with 0 errors; format check passes (apply `dotnet format` without
`--verify-no-changes` if it reports diffs, then re-verify).

#### Step 2 — commit

```
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletDocumentRepositoryPagedTests.cs
git commit -m "Add GetDocumentsPagedAsync filter tests (new integration test file)

GetDocumentsPagedAsync previously had zero tests against the real
repository — it was only ever exercised through handler tests that mock
ILeafletDocumentRepository entirely. This adds a new sibling integration
test file (same Testcontainers/pgvector pattern as
LeafletRepositoryIntegrationTests.cs) covering the filenameFilter (partial,
case-sensitive, wildcard-escaping, no-match), statusFilter (all three enum
values), contentTypeFilter (exact match), and all three filters combined
with AND semantics. Test-only change; Category=Integration."
```

---

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
