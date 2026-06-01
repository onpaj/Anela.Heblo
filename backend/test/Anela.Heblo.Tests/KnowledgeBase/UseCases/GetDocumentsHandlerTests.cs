using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class GetDocumentsHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    private static KnowledgeBaseDocument MakeDoc(
        string filename = "test.pdf",
        DocumentStatus status = DocumentStatus.Indexed,
        string contentType = "application/pdf",
        DateTime? createdAt = null,
        DateTime? indexedAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            SourcePath = $"/inbox/{filename}",
            ContentType = contentType,
            ContentHash = Guid.NewGuid().ToString(),
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IndexedAt = indexedAt ?? DateTime.UtcNow,
        };

    private void SetupRepository(
        List<KnowledgeBaseDocument> docs,
        int totalCount = -1,
        Dictionary<Guid, Guid>? firstChunkMap = null)
    {
        _repository
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<DocumentStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((docs, totalCount < 0 ? docs.Count : totalCount));

        _repository
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstChunkMap ?? new Dictionary<Guid, Guid>());
    }

    [Fact]
    public async Task Handle_ReturnsMappedDocuments()
    {
        var doc = MakeDoc("report.pdf", DocumentStatus.Indexed);
        SetupRepository([doc]);

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(new GetDocumentsRequest(), default);

        Assert.True(result.Success);
        Assert.Single(result.Documents);
        Assert.Equal("report.pdf", result.Documents[0].Filename);
        Assert.Equal("indexed", result.Documents[0].Status);
    }

    [Fact]
    public async Task Handle_ReturnsPaginationMetadata()
    {
        var docs = Enumerable.Range(1, 10).Select(i => MakeDoc($"doc{i}.pdf")).ToList();
        SetupRepository(docs, totalCount: 50);

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(
            new GetDocumentsRequest { PageNumber = 2, PageSize = 10 },
            default);

        Assert.Equal(50, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(5, result.TotalPages);
    }

    [Theory]
    [InlineData(5, 20)]   // 5 not in allowed list → fallback 20
    [InlineData(0, 20)]   // 0 not allowed → fallback 20
    [InlineData(100, 20)] // 100 not allowed → fallback 20
    [InlineData(10, 10)]  // 10 allowed
    [InlineData(50, 50)]  // 50 allowed
    public async Task Handle_ClampsInvalidPageSize(int requested, int expected)
    {
        SetupRepository([]);

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(
            new GetDocumentsRequest { PageSize = requested },
            default);

        Assert.Equal(expected, result.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]   // 0 < 1 → clamp to 1
    [InlineData(-5, 1)]  // negative → clamp to 1
    [InlineData(3, 3)]   // valid stays
    public async Task Handle_ClampsPageNumberToMinimumOne(int requested, int expected)
    {
        SetupRepository([]);

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(
            new GetDocumentsRequest { PageNumber = requested },
            default);

        Assert.Equal(expected, result.PageNumber);
    }

    [Theory]
    [InlineData("UnknownColumn", "CreatedAt")] // unknown → fallback
    [InlineData("", "CreatedAt")]              // empty → fallback
    [InlineData("Filename", "Filename")]       // valid stays
    [InlineData("Status", "Status")]           // valid stays
    [InlineData("IndexedAt", "IndexedAt")]     // valid stays
    [InlineData("CreatedAt", "CreatedAt")]     // valid stays
    public async Task Handle_FallsBackInvalidSortBy(string requested, string expected)
    {
        string? capturedSortBy = null;
        _repository
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<DocumentStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, DocumentStatus?, string?, string, bool, int, int, CancellationToken>(
                (_, _, _, sortBy, _, _, _, _) => capturedSortBy = sortBy)
            .ReturnsAsync((new List<KnowledgeBaseDocument>(), 0));
        _repository
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = new GetDocumentsHandler(_repository.Object);
        await handler.Handle(new GetDocumentsRequest { SortBy = requested }, default);

        Assert.Equal(expected, capturedSortBy);
    }

    [Theory]
    [InlineData("indexed", DocumentStatus.Indexed)]
    [InlineData("INDEXED", DocumentStatus.Indexed)]
    [InlineData("processing", DocumentStatus.Processing)]
    [InlineData("failed", DocumentStatus.Failed)]
    public async Task Handle_ParsesStatusFilterCaseInsensitive(string statusString, DocumentStatus expected)
    {
        DocumentStatus? capturedStatus = null;
        _repository
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<DocumentStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, DocumentStatus?, string?, string, bool, int, int, CancellationToken>(
                (_, status, _, _, _, _, _, _) => capturedStatus = status)
            .ReturnsAsync((new List<KnowledgeBaseDocument>(), 0));
        _repository
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = new GetDocumentsHandler(_repository.Object);
        await handler.Handle(new GetDocumentsRequest { StatusFilter = statusString }, default);

        Assert.Equal(expected, capturedStatus);
    }

    [Theory]
    [InlineData("unknown_status")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Handle_IgnoresInvalidStatusFilter(string? statusString)
    {
        DocumentStatus? capturedStatus = new DocumentStatus();
        _repository
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<DocumentStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, DocumentStatus?, string?, string, bool, int, int, CancellationToken>(
                (_, status, _, _, _, _, _, _) => capturedStatus = status)
            .ReturnsAsync((new List<KnowledgeBaseDocument>(), 0));
        _repository
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = new GetDocumentsHandler(_repository.Object);
        await handler.Handle(new GetDocumentsRequest { StatusFilter = statusString }, default);

        Assert.Null(capturedStatus);
    }

    [Fact]
    public async Task Handle_PopulatesFirstChunkId_WhenChunkExists()
    {
        var doc = MakeDoc("chat.txt", DocumentStatus.Indexed);
        var chunkId = Guid.NewGuid();
        SetupRepository([doc], firstChunkMap: new Dictionary<Guid, Guid> { [doc.Id] = chunkId });

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(new GetDocumentsRequest(), default);

        Assert.Equal(chunkId, result.Documents[0].FirstChunkId);
    }

    [Fact]
    public async Task Handle_FirstChunkIdIsNull_WhenNoChunkExists()
    {
        var doc = MakeDoc("processing.txt", DocumentStatus.Processing);
        SetupRepository([doc]);

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(new GetDocumentsRequest(), default);

        Assert.Null(result.Documents[0].FirstChunkId);
    }

    [Fact]
    public async Task Handle_TotalPagesRoundsUp()
    {
        SetupRepository([], totalCount: 25);

        var handler = new GetDocumentsHandler(_repository.Object);
        var result = await handler.Handle(
            new GetDocumentsRequest { PageSize = 10 },
            default);

        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public async Task Handle_PassesAllFiltersToRepository()
    {
        string? capturedFilename = null;
        DocumentStatus? capturedStatus = null;
        string? capturedContentType = null;
        string? capturedSortBy = null;
        bool? capturedSortDesc = null;
        int? capturedPage = null;
        int? capturedPageSize = null;

        _repository
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<string?>(),
                It.IsAny<DocumentStatus?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, DocumentStatus?, string?, string, bool, int, int, CancellationToken>(
                (fn, st, ct, sb, sd, pn, ps, _) =>
                {
                    capturedFilename = fn;
                    capturedStatus = st;
                    capturedContentType = ct;
                    capturedSortBy = sb;
                    capturedSortDesc = sd;
                    capturedPage = pn;
                    capturedPageSize = ps;
                })
            .ReturnsAsync((new List<KnowledgeBaseDocument>(), 0));
        _repository
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = new GetDocumentsHandler(_repository.Object);
        await handler.Handle(new GetDocumentsRequest
        {
            FilenameFilter = "report",
            StatusFilter = "indexed",
            ContentTypeFilter = "application/pdf",
            SortBy = "Filename",
            SortDescending = false,
            PageNumber = 3,
            PageSize = 50,
        }, default);

        Assert.Equal("report", capturedFilename);
        Assert.Equal(DocumentStatus.Indexed, capturedStatus);
        Assert.Equal("application/pdf", capturedContentType);
        Assert.Equal("Filename", capturedSortBy);
        Assert.False(capturedSortDesc);
        Assert.Equal(3, capturedPage);
        Assert.Equal(50, capturedPageSize);
    }
}
