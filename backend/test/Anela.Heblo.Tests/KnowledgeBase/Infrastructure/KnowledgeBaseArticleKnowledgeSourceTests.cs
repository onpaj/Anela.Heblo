using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Infrastructure;

public class KnowledgeBaseArticleKnowledgeSourceTests
{
    private readonly Mock<IMediator> _mediator = new();

    private KnowledgeBaseArticleKnowledgeSource CreateAdapter() =>
        new(_mediator.Object);

    [Fact]
    public async Task SearchAsync_DispatchesSearchDocumentsRequest_WithCorrectQueryAndTopK()
    {
        // Arrange
        var adapter = CreateAdapter();
        const string query = "test query";
        const int topK = 5;
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        await adapter.SearchAsync(query, topK, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<SearchDocumentsRequest>(r =>
                r.Query == query &&
                r.TopK == topK),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MapsFourFields_FromChunkResultToArticleKnowledgeChunk()
    {
        // Arrange
        var adapter = CreateAdapter();
        var chunkId = Guid.NewGuid();
        const string sourceFilename = "doc.pdf";
        const string content = "content text";
        const double score = 0.95;
        var documentId = Guid.NewGuid();
        const string sourcePath = "/some/path";

        var chunkResult = new ChunkResult
        {
            ChunkId = chunkId,
            SourceFilename = sourceFilename,
            Content = content,
            Score = score,
            DocumentId = documentId,
            SourcePath = sourcePath,
        };

        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [chunkResult] });

        // Act
        var result = await adapter.SearchAsync("query", 3, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].ChunkId.Should().Be(chunkId);
        result[0].SourceFilename.Should().Be(sourceFilename);
        result[0].Content.Should().Be(content);
        result[0].Score.Should().Be(score);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenNoChunks()
    {
        // Arrange
        var adapter = CreateAdapter();
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        var result = await adapter.SearchAsync("query", 5, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_PropagatesCancellationToken()
    {
        // Arrange
        var adapter = CreateAdapter();
        var cts = new CancellationTokenSource();
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        await adapter.SearchAsync("query", 5, cts.Token);

        // Assert
        _mediator.Verify(m => m.Send(
            It.IsAny<SearchDocumentsRequest>(),
            cts.Token), Times.Once);
    }
}
