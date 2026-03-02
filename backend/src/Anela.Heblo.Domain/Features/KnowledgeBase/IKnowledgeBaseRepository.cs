namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public interface IKnowledgeBaseRepository
{
    Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default);
    Task AddChunksAsync(IEnumerable<KnowledgeBaseChunk> chunks, CancellationToken ct = default);
    Task<List<KnowledgeBaseDocument>> GetAllDocumentsAsync(CancellationToken ct = default);
    Task<List<(KnowledgeBaseChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default);
    Task<bool> DocumentExistsBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
