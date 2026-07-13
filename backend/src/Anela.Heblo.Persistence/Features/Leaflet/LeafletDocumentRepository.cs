using System.Text;
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletDocumentRepository : ILeafletDocumentRepository
{
    private readonly ApplicationDbContext _context;

    // 7 params/row × 1000 rows = 7,000 params — comfortably under Npgsql's 65,535 ceiling.
    private const int MaxRowsPerBatch = 1000;

    public LeafletDocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddDocumentAsync(LeafletDocument document, CancellationToken ct = default)
    {
        _context.LeafletDocuments.Add(document);
        await _context.SaveChangesAsync(ct);  // EAGER COMMIT — key behavior change vs LeafletRepository
    }

    public async Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
            return;

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        for (var offset = 0; offset < chunkList.Count; offset += MaxRowsPerBatch)
        {
            var batch = chunkList.Skip(offset).Take(MaxRowsPerBatch).ToList();

            // Column list MUST mirror LeafletChunkConfiguration. See memory/gotchas/raw-sql-insert-must-match-ef-mapping.md
            var sql = new StringBuilder(
                "INSERT INTO \"LeafletChunks\" (\"Id\", \"DocumentId\", \"ChunkIndex\", \"Content\", \"Summary\", \"WordCount\", \"Embedding\") VALUES ");

            await using var cmd = new NpgsqlCommand { Connection = connection };

            for (var i = 0; i < batch.Count; i++)
            {
                var chunk = batch[i];
                if (i > 0)
                    sql.Append(", ");
                sql.Append($"(@id{i}, @documentId{i}, @chunkIndex{i}, @content{i}, @summary{i}, @wordCount{i}, @embedding{i})");

                cmd.Parameters.AddWithValue($"id{i}", chunk.Id);
                cmd.Parameters.AddWithValue($"documentId{i}", chunk.DocumentId);
                cmd.Parameters.AddWithValue($"chunkIndex{i}", chunk.ChunkIndex);
                cmd.Parameters.AddWithValue($"content{i}", chunk.Content);
                cmd.Parameters.AddWithValue($"summary{i}", chunk.Summary);
                cmd.Parameters.AddWithValue($"wordCount{i}", chunk.WordCount);
                cmd.Parameters.AddWithValue($"embedding{i}", new Vector(chunk.Embedding));
            }

            sql.Append(" ON CONFLICT (\"Id\") DO NOTHING");
            cmd.CommandText = sql.ToString();

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
            SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content", c."Summary", c."WordCount",
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
                Summary = reader.GetString(4),
                WordCount = reader.GetInt32(5),
                Embedding = [],
                Document = new LeafletDocument
                {
                    Id = documentId,
                    Filename = reader.GetString(6),
                    SourcePath = reader.GetString(7),
                }
            };

            results.Add((chunk, reader.GetDouble(8)));
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
}
