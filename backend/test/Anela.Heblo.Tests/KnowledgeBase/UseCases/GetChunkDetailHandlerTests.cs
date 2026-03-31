using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class GetChunkDetailHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    private static KnowledgeBaseChunk MakeChunk(
        Guid? id = null,
        string content = "full conversation text",
        string summary = "AI-generated summary",
        int chunkIndex = 0,
        DocumentType documentType = DocumentType.Conversation) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ChunkIndex = chunkIndex,
            Content = content,
            Summary = summary,
            DocumentType = documentType,
            Embedding = [],
            Document = new KnowledgeBaseDocument
            {
                Id = Guid.NewGuid(),
                Filename = "conversation-2024.txt",
                SourcePath = "/inbox/conversation-2024.txt",
                ContentType = "text/plain",
                ContentHash = "abc123",
                Status = DocumentStatus.Indexed,
                DocumentType = documentType,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                IndexedAt = DateTime.UtcNow,
            }
        };

    [Fact]
    public async Task Handle_ReturnsChunkDetail_WhenChunkExists()
    {
        var chunkId = Guid.NewGuid();
        var chunk = MakeChunk(id: chunkId, content: "full text", summary: "summary");

        _repository
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunk);

        var handler = new GetChunkDetailHandler(_repository.Object);
        var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

        Assert.True(result.Success);
        Assert.Equal(chunkId, result.ChunkId);
        Assert.Equal("full text", result.Content);
        Assert.Equal("summary", result.Summary);
        Assert.Equal("conversation-2024.txt", result.Filename);
        Assert.Equal(DocumentType.Conversation, result.DocumentType);
        Assert.NotNull(result.IndexedAt);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenChunkDoesNotExist()
    {
        var chunkId = Guid.NewGuid();

        _repository
            .Setup(r => r.GetChunkByIdAsync(chunkId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeBaseChunk?)null);

        var handler = new GetChunkDetailHandler(_repository.Object);
        var result = await handler.Handle(new GetChunkDetailRequest { ChunkId = chunkId }, default);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.KnowledgeBaseChunkNotFound, result.ErrorCode);
    }
}
