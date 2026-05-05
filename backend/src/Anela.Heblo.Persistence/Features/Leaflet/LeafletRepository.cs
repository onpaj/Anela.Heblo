using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletRepository : ILeafletRepository
{
    private readonly ApplicationDbContext _context;

    public LeafletRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddDocumentAsync(LeafletDocument document, CancellationToken ct = default)
    {
        _context.LeafletDocuments.Add(document);
        return Task.CompletedTask;
    }

    public async Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        foreach (var chunk in chunkList)
        {
            var embedding = new Vector(chunk.Embedding);
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "WordCount", "Embedding")
                VALUES (@id, @documentId, @chunkIndex, @content, @wordCount, @embedding)
                ON CONFLICT ("Id") DO NOTHING
                """,
                connection);

            cmd.Parameters.AddWithValue("id", chunk.Id);
            cmd.Parameters.AddWithValue("documentId", chunk.DocumentId);
            cmd.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            cmd.Parameters.AddWithValue("content", chunk.Content);
            cmd.Parameters.AddWithValue("wordCount", chunk.WordCount);
            cmd.Parameters.AddWithValue("embedding", embedding);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<LeafletDocument?> GetByHashAsync(string contentHash, CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);
    }

    public async Task<LeafletDocument?> GetBySourcePathAsync(string sourcePath, CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourcePath == sourcePath, ct);
    }

    public async Task<LeafletDocument?> GetByGraphItemIdAsync(string driveId, string graphItemId, CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DriveId == driveId && x.GraphItemId == graphItemId, ct);
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding, int topK, CancellationToken ct = default)
    {
        var vector = new Vector(queryEmbedding);

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // Cosine distance: lower = more similar. Score = 1 - distance.
        // CommandTimeout set to 120s — vector similarity search can be slow without a warm HNSW index.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content", c."WordCount",
                   d."Filename", d."SourcePath",
                   1 - (c."Embedding" <=> @embedding) AS "Score"
            FROM "LeafletChunks" c
            JOIN "LeafletDocuments" d ON d."Id" = c."DocumentId"
            ORDER BY c."Embedding" <=> @embedding
            LIMIT @topK
            """,
            connection)
        {
            CommandTimeout = 120
        };

        cmd.Parameters.AddWithValue("embedding", vector);
        cmd.Parameters.AddWithValue("topK", topK);

        var results = new List<(LeafletChunk Chunk, double Score)>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var documentId = reader.GetGuid(1);

            var chunk = new LeafletChunk
            {
                Id = reader.GetGuid(0),
                DocumentId = documentId,
                ChunkIndex = reader.GetInt32(2),
                Content = reader.GetString(3),
                WordCount = reader.GetInt32(4),
                Embedding = [],
                Document = new LeafletDocument
                {
                    Id = documentId,
                    Filename = reader.GetString(5),
                    SourcePath = reader.GetString(6),
                }
            };

            results.Add((chunk, reader.GetDouble(7)));
        }

        return results;
    }

    public async Task UpdateSourcePathAsync(Guid documentId, string newPath, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SourcePath, newPath), ct);
    }

    public async Task UpdateGraphItemIdAsync(Guid documentId, string driveId, string graphItemId, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.DriveId, driveId)
                .SetProperty(d => d.GraphItemId, graphItemId), ct);
    }

    public async Task UpdateStatusAsync(Guid documentId, LeafletDocumentStatus status, DateTime? indexedAt, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.IndexedAt, indexedAt), ct);
    }

    public async Task<(IReadOnlyList<LeafletDocument> Items, int Total)> GetDocumentsPagedAsync(
        int pageNumber, int pageSize, string sortBy, bool sortDescending,
        string? filenameFilter, LeafletDocumentStatus? statusFilter, string? contentTypeFilter,
        CancellationToken ct = default)
    {
        var query = _context.LeafletDocuments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filenameFilter))
        {
            var escaped = filenameFilter.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(d => EF.Functions.Like(d.Filename, $"%{escaped}%", "\\"));
        }

        if (statusFilter.HasValue)
            query = query.Where(d => d.Status == statusFilter.Value);

        if (!string.IsNullOrEmpty(contentTypeFilter))
            query = query.Where(d => d.ContentType == contentTypeFilter);

        query = sortBy switch
        {
            "Filename" => sortDescending
                ? query.OrderByDescending(d => d.Filename)
                : query.OrderBy(d => d.Filename),
            "Status" => sortDescending
                ? query.OrderByDescending(d => d.Status)
                : query.OrderBy(d => d.Status),
            "IndexedAt" => sortDescending
                ? query.OrderByDescending(d => d.IndexedAt)
                : query.OrderBy(d => d.IndexedAt),
            _ => sortDescending
                ? query.OrderByDescending(d => d.IngestedAt)
                : query.OrderBy(d => d.IngestedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<string>> GetDistinctContentTypesAsync(CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .Select(d => d.ContentType)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    public async Task<LeafletChunk?> GetChunkByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.LeafletChunks
            .AsNoTracking()
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetFirstChunkIdsByDocumentIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return await _context.LeafletChunks
            .Where(c => idList.Contains(c.DocumentId))
            .GroupBy(c => c.DocumentId)
            .Select(g => new { DocumentId = g.Key, ChunkId = g.OrderBy(c => c.ChunkIndex).First().Id })
            .ToDictionaryAsync(x => x.DocumentId, x => x.ChunkId, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    public async Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken)
    {
        _context.LeafletGenerations.Add(generation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken)
        => await _context.LeafletGenerations.FindAsync([id], cancellationToken);

    public async Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback, string? userId, string sortBy, bool descending,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.LeafletGenerations.AsQueryable();

        if (hasFeedback == true)
            query = query.Where(g => g.PrecisionScore != null || g.StyleScore != null);
        else if (hasFeedback == false)
            query = query.Where(g => g.PrecisionScore == null && g.StyleScore == null);

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(g => g.UserId == userId);

        query = (sortBy, descending) switch
        {
            ("PrecisionScore", true) => query.OrderByDescending(g => g.PrecisionScore),
            ("PrecisionScore", false) => query.OrderBy(g => g.PrecisionScore),
            ("StyleScore", true) => query.OrderByDescending(g => g.StyleScore),
            ("StyleScore", false) => query.OrderBy(g => g.StyleScore),
            (_, true) => query.OrderByDescending(g => g.CreatedAt),
            _ => query.OrderBy(g => g.CreatedAt),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
    {
        var total = await _context.LeafletGenerations.CountAsync(cancellationToken);
        var withFeedback = await _context.LeafletGenerations
            .CountAsync(g => g.PrecisionScore != null || g.StyleScore != null, cancellationToken);
        var avgPrecision = await _context.LeafletGenerations
            .Where(g => g.PrecisionScore != null)
            .AverageAsync(g => (double?)g.PrecisionScore, cancellationToken);
        var avgStyle = await _context.LeafletGenerations
            .Where(g => g.StyleScore != null)
            .AverageAsync(g => (double?)g.StyleScore, cancellationToken);
        return new LeafletFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
    }
}
