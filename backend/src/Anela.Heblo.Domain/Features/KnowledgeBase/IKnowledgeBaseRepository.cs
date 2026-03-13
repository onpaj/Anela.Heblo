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
    Task<KnowledgeBaseDocument?> GetDocumentByHashAsync(string contentHash, CancellationToken ct = default);
    Task<KnowledgeBaseDocument?> GetDocumentBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task UpdateDocumentSourcePathAsync(Guid documentId, string newSourcePath, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task SaveQuestionLogAsync(KnowledgeBaseQuestionLog log, CancellationToken ct = default);
    Task<KnowledgeBaseQuestionLog?> GetQuestionLogByIdAsync(Guid id, CancellationToken ct = default);
}
