using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.KnowledgeBase.Infrastructure;

public class KnowledgeBaseLeafletSourceAdapterTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    private KnowledgeBaseLeafletSourceAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task SearchSimilarAsync_forwards_query_to_repository()
    {
        var vector = new[] { 0.1f, 0.2f, 0.3f };
        const int topK = 5;
        var ct = CancellationToken.None;

        _repository
            .Setup(r => r.SearchSimilarAsync(vector, topK, ct))
            .ReturnsAsync(new List<(KnowledgeBaseChunk Chunk, double Score)>());

        var adapter = CreateAdapter();

        await adapter.SearchSimilarAsync(vector, topK, ct);

        _repository.Verify(
            r => r.SearchSimilarAsync(vector, topK, ct),
            Times.Once);
    }

    [Fact]
    public async Task SearchSimilarAsync_projects_chunks_to_KnowledgeSearchResult()
    {
        var chunk1 = new KnowledgeBaseChunk { Id = Guid.NewGuid(), Content = "first chunk content" };
        var chunk2 = new KnowledgeBaseChunk { Id = Guid.NewGuid(), Content = "second chunk content" };

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(KnowledgeBaseChunk Chunk, double Score)>
            {
                (chunk1, 0.92),
                (chunk2, 0.71),
            });

        var adapter = CreateAdapter();

        var results = await adapter.SearchSimilarAsync(new[] { 0f }, 10, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Content.Should().Be("first chunk content");
        results[0].Score.Should().Be(0.92);
        results[1].Content.Should().Be("second chunk content");
        results[1].Score.Should().Be(0.71);
    }

    [Fact]
    public async Task SearchSimilarAsync_returns_empty_list_when_repository_returns_empty()
    {
        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(KnowledgeBaseChunk Chunk, double Score)>());

        var adapter = CreateAdapter();

        var results = await adapter.SearchSimilarAsync(new[] { 0f }, 10, CancellationToken.None);

        results.Should().BeEmpty();
    }
}
