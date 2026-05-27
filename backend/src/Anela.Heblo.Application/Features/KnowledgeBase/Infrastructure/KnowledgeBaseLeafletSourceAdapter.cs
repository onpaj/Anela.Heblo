using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseLeafletSourceAdapter : ILeafletKnowledgeSource
{
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseLeafletSourceAdapter(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var hits = await _repository.SearchSimilarAsync(queryEmbedding, topK, cancellationToken);
        return hits
            .Select(h => new KnowledgeSearchResult
            {
                Content = h.Chunk.Content,
                Score = h.Score,
            })
            .ToList();
    }
}
