using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class SearchDocumentsHandlerTests
{
    private readonly Mock<IEmbeddingService> _embeddingService = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    [Fact]
    public async Task Handle_ReturnsChunksOrderedByScore()
    {
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _embeddingService
            .Setup(s => s.GenerateEmbeddingAsync("phenoxyethanol concentration", default))
            .ReturnsAsync(queryEmbedding);

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            ChunkIndex = 0,
            Content = "Phenoxyethanol max 1.0% in Annex V",
            Embedding = queryEmbedding,
            Document = new KnowledgeBaseDocument { Filename = "EU_reg.pdf", SourcePath = "/inbox/EU_reg.pdf" }
        };

        _repository
            .Setup(r => r.SearchSimilarAsync(queryEmbedding, 5, default))
            .ReturnsAsync([(chunk, 0.95)]);

        var handler = new SearchDocumentsHandler(_embeddingService.Object, _repository.Object);
        var result = await handler.Handle(
            new SearchDocumentsRequest { Query = "phenoxyethanol concentration", TopK = 5 },
            default);

        Assert.Single(result.Chunks);
        Assert.Equal("Phenoxyethanol max 1.0% in Annex V", result.Chunks[0].Content);
        Assert.Equal(0.95, result.Chunks[0].Score);
        Assert.Equal("EU_reg.pdf", result.Chunks[0].SourceFilename);
    }
}
