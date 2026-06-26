namespace Anela.Heblo.Domain.Features.Leaflet;

public interface ILeafletDocumentRepository
{
    Task AddDocumentAsync(LeafletDocument document, CancellationToken ct = default);
    Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default);
    Task<LeafletDocument?> GetByHashAsync(string contentHash, CancellationToken ct = default);
    Task<LeafletDocument?> GetBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task<LeafletDocument?> GetByGraphItemIdAsync(string driveId, string graphItemId, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
    Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding, int topK, CancellationToken ct = default);
    Task UpdateSourcePathAsync(Guid documentId, string newPath, CancellationToken ct = default);
    Task UpdateGraphItemIdAsync(Guid documentId, string driveId, string graphItemId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid documentId, LeafletDocumentStatus status, DateTime? indexedAt, CancellationToken ct = default);
    Task<(IReadOnlyList<LeafletDocument> Items, int Total)> GetDocumentsPagedAsync(
        int pageNumber, int pageSize, string sortBy, bool sortDescending,
        string? filenameFilter, LeafletDocumentStatus? statusFilter, string? contentTypeFilter,
        CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctContentTypesAsync(CancellationToken ct = default);
    Task<LeafletChunk?> GetChunkByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Guid>> GetFirstChunkIdsByDocumentIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
