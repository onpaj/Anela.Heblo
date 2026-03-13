using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class SearchDocumentsHandlerTests
{
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    [Fact]
    public async Task Handle_ReturnsChunksOrderedByScore()
    {
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        var embeddingVector = new ReadOnlyMemory<float>(queryEmbedding);
        var generatedEmbeddings = new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(embeddingVector)]);

        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                default))
            .ReturnsAsync(generatedEmbeddings);

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
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([(chunk, 0.95)]);

        var handler = new SearchDocumentsHandler(_embeddingGenerator.Object, _repository.Object);
        var result = await handler.Handle(
            new SearchDocumentsRequest { Query = "phenoxyethanol concentration", TopK = 5 },
            default);

        Assert.Single(result.Chunks);
        Assert.Equal("Phenoxyethanol max 1.0% in Annex V", result.Chunks[0].Content);
        Assert.Equal(0.95, result.Chunks[0].Score);
        Assert.Equal("EU_reg.pdf", result.Chunks[0].SourceFilename);
    }
}
