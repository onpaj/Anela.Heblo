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
