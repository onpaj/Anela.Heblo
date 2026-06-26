using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletDocumentsHandlerTests
{
    private readonly Mock<ILeafletDocumentRepository> _repoMock = new();

    private GetLeafletDocumentsHandler CreateHandler() =>
        new(_repoMock.Object);

    private void SetupPagedRepo(IReadOnlyList<LeafletDocument> docs, int total)
    {
        _repoMock
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<LeafletDocumentStatus?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((docs, total));

        _repoMock
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());
    }

    [Fact]
    public async Task Handle_happy_path_returns_paged_list()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var docs = new List<LeafletDocument>
        {
            new()
            {
                Id = docId,
                Filename = "catalog.pdf",
                Status = LeafletDocumentStatus.Indexed,
                ContentType = "application/pdf",
                IngestedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IndexedAt = new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            },
        };

        SetupPagedRepo(docs, 1);

        var handler = CreateHandler();
        var request = new GetLeafletDocumentsRequest { PageNumber = 1, PageSize = 20, SortBy = "IngestedAt" };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.TotalCount.Should().Be(1);
        response.Documents.Should().HaveCount(1);
        response.Documents[0].Id.Should().Be(docId);
        response.Documents[0].Filename.Should().Be("catalog.pdf");
        response.Documents[0].Status.Should().Be("indexed");
    }

    [Fact]
    public async Task Handle_invalid_sort_column_falls_back_to_IngestedAt()
    {
        // Arrange
        string? capturedSortBy = null;
        _repoMock
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<LeafletDocumentStatus?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<int, int, string, bool, string?, LeafletDocumentStatus?, string?, CancellationToken>(
                (_, _, sortBy, _, _, _, _, _) => capturedSortBy = sortBy)
            .Returns(Task.FromResult(((IReadOnlyList<LeafletDocument>)[], 0)));

        _repoMock
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = CreateHandler();
        var request = new GetLeafletDocumentsRequest { SortBy = "NonExistentColumn" };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedSortBy.Should().Be("IngestedAt");
    }

    [Fact]
    public async Task Handle_invalid_page_size_falls_back_to_20()
    {
        // Arrange
        int? capturedPageSize = null;
        _repoMock
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<LeafletDocumentStatus?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<int, int, string, bool, string?, LeafletDocumentStatus?, string?, CancellationToken>(
                (_, pageSize, _, _, _, _, _, _) => capturedPageSize = pageSize)
            .Returns(Task.FromResult(((IReadOnlyList<LeafletDocument>)[], 0)));

        _repoMock
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = CreateHandler();
        var request = new GetLeafletDocumentsRequest { PageSize = 99 };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedPageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_valid_status_filter_is_passed_to_repo()
    {
        // Arrange
        LeafletDocumentStatus? capturedStatusFilter = null;
        _repoMock
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<LeafletDocumentStatus?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<int, int, string, bool, string?, LeafletDocumentStatus?, string?, CancellationToken>(
                (_, _, _, _, _, statusFilter, _, _) => capturedStatusFilter = statusFilter)
            .Returns(Task.FromResult(((IReadOnlyList<LeafletDocument>)[], 0)));

        _repoMock
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid>());

        var handler = CreateHandler();
        var request = new GetLeafletDocumentsRequest { StatusFilter = "Indexed" };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedStatusFilter.Should().Be(LeafletDocumentStatus.Indexed);
    }

    [Fact]
    public async Task Handle_empty_page_returns_empty_documents_list()
    {
        // Arrange
        SetupPagedRepo([], 0);

        var handler = CreateHandler();
        var request = new GetLeafletDocumentsRequest { PageNumber = 5 };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Documents.Should().BeEmpty();
        response.TotalCount.Should().Be(0);

        _repoMock.Verify(
            r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_first_chunk_id_is_mapped_when_available()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var docs = new List<LeafletDocument>
        {
            new() { Id = docId, Filename = "test.pdf", Status = LeafletDocumentStatus.Indexed },
        };

        _repoMock
            .Setup(r => r.GetDocumentsPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<LeafletDocumentStatus?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((docs, 1));

        _repoMock
            .Setup(r => r.GetFirstChunkIdsByDocumentIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Guid> { [docId] = chunkId });

        var handler = CreateHandler();
        var request = new GetLeafletDocumentsRequest();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Documents[0].FirstChunkId.Should().Be(chunkId);
    }
}
