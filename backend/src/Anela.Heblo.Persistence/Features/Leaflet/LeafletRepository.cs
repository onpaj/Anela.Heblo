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

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
