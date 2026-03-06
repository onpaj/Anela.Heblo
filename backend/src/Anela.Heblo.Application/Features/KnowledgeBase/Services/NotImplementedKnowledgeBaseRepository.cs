using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Placeholder implementation of IKnowledgeBaseRepository.
/// This will be replaced by the real EF Core implementation in Phase 4.
/// </summary>
internal class NotImplementedKnowledgeBaseRepository : IKnowledgeBaseRepository
{
    public Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");

    public Task AddChunksAsync(IEnumerable<KnowledgeBaseChunk> chunks, CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");

    public Task<List<KnowledgeBaseDocument>> GetAllDocumentsAsync(CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");

    public Task<List<(KnowledgeBaseChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");

    public Task<KnowledgeBaseDocument?> GetDocumentByHashAsync(string contentHash, CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");

    public Task UpdateDocumentSourcePathAsync(Guid documentId, string newSourcePath, CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");

    public Task SaveChangesAsync(CancellationToken ct = default)
        => throw new NotImplementedException("KnowledgeBase repository will be implemented in Phase 4.");
}
