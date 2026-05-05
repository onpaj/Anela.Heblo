using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletChunkDetailHandlerTests
{
    private readonly Mock<ILeafletRepository> _repoMock = new();

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
}
