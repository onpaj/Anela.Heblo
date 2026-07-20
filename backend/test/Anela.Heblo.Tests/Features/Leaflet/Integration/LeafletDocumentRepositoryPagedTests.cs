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
}
