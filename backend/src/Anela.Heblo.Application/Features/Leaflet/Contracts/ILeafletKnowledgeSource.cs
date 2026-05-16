namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

/// <summary>
/// Leaflet-owned read-only abstraction over the knowledge base vector index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface ILeafletKnowledgeSource
{
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken);
}
