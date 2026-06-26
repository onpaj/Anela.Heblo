using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletChunkDetailHandlerTests
{
    private readonly Mock<ILeafletDocumentRepository> _repoMock = new();

    private GetLeafletChunkDetailHandler CreateHandler() =>
        new(_repoMock.Object);

    [Fact]
    public async Task Handle_returns_chunk_detail_when_chunk_exists()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var indexedAt = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Utc);

        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = documentId,
            ChunkIndex = 2,
            Content = "This is the chunk content.",
            Summary = "expected summary",
            Document = new LeafletDocument
            {
                Id = documentId,
                Filename = "brochure.pdf",
                IndexedAt = indexedAt,
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();
        var request = new GetLeafletChunkDetailRequest { ChunkId = chunkId };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ChunkId.Should().Be(chunkId);
        response.DocumentId.Should().Be(documentId);
        response.Filename.Should().Be("brochure.pdf");
        response.ChunkIndex.Should().Be(2);
        response.Content.Should().Be("This is the chunk content.");
        response.Summary.Should().Be("expected summary");
        response.IndexedAt.Should().Be(indexedAt);
    }

    [Fact]
    public async Task Handle_returns_error_response_when_chunk_not_found()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletChunk?)null);

        var handler = CreateHandler();
        var request = new GetLeafletChunkDetailRequest { ChunkId = chunkId };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LeafletChunkNotFound);
    }

    [Fact]
    public async Task Handle_returns_sharepoint_source_path_when_document_sourced_from_sharepoint()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "c",
            Summary = "s",
            Document = new LeafletDocument
            {
                Id = Guid.NewGuid(),
                Filename = "leaflet.pdf",
                SourcePath = "https://anelacz.sharepoint.com/sites/x/leaflet.pdf",
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletChunkDetailRequest { ChunkId = chunkId }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.SourcePath.Should().Be("https://anelacz.sharepoint.com/sites/x/leaflet.pdf");
    }

    [Fact]
    public async Task Handle_returns_synthetic_upload_path_when_document_manually_uploaded()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "c",
            Summary = "s",
            Document = new LeafletDocument
            {
                Id = Guid.NewGuid(),
                Filename = "uploaded.pdf",
                SourcePath = "upload/xyz-9/uploaded.pdf",
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletChunkDetailRequest { ChunkId = chunkId }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.SourcePath.Should().Be("upload/xyz-9/uploaded.pdf");
    }

    [Fact]
    public async Task Handle_returns_empty_string_when_document_has_no_source_path()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var chunk = new LeafletChunk
        {
            Id = chunkId,
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "c",
            Summary = "s",
            Document = new LeafletDocument
            {
                Id = Guid.NewGuid(),
                Filename = "no-source.pdf",
                SourcePath = string.Empty,
            },
        };

        _repoMock
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletChunkDetailRequest { ChunkId = chunkId }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.SourcePath.Should().Be(string.Empty);
    }
}
