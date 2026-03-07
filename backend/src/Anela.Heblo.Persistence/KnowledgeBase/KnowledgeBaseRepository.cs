using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace Anela.Heblo.Persistence.KnowledgeBase;

public class KnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly ApplicationDbContext _context;

    public KnowledgeBaseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default)
    {
        _context.KnowledgeBaseDocuments.Add(document);
        await Task.CompletedTask;
    }

    public async Task AddChunksAsync(IEnumerable<KnowledgeBaseChunk> chunks, CancellationToken ct = default)
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
                INSERT INTO dbo."KnowledgeBaseChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "Embedding")
                VALUES (@id, @documentId, @chunkIndex, @content, @embedding)
                """,
                connection);

            cmd.Parameters.AddWithValue("id", chunk.Id);
            cmd.Parameters.AddWithValue("documentId", chunk.DocumentId);
            cmd.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            cmd.Parameters.AddWithValue("content", chunk.Content);
            cmd.Parameters.AddWithValue("embedding", embedding);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<List<KnowledgeBaseDocument>> GetAllDocumentsAsync(CancellationToken ct = default)
    {
        return await _context.KnowledgeBaseDocuments
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<(KnowledgeBaseChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken ct = default)
    {
        var vector = new Vector(queryEmbedding);

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // Cosine distance: lower = more similar. Score = 1 - distance.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content",
                   1 - (c."Embedding" <=> @embedding) AS "Score",
                   d."Filename", d."SourcePath"
            FROM dbo."KnowledgeBaseChunks" c
            JOIN dbo."KnowledgeBaseDocuments" d ON c."DocumentId" = d."Id"
            ORDER BY c."Embedding" <=> @embedding
            LIMIT @topK
            """,
            connection);

        cmd.Parameters.AddWithValue("embedding", vector);
        cmd.Parameters.AddWithValue("topK", topK);

        var results = new List<(KnowledgeBaseChunk Chunk, double Score)>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var chunkId = reader.GetGuid(0);
            var documentId = reader.GetGuid(1);
            var chunkIndex = reader.GetInt32(2);
            var content = reader.GetString(3);
            var score = reader.GetDouble(4);
            var filename = reader.GetString(5);
            var sourcePath = reader.GetString(6);

            var document = new KnowledgeBaseDocument
            {
                Id = documentId,
                Filename = filename,
                SourcePath = sourcePath
            };

            var chunk = new KnowledgeBaseChunk
            {
                Id = chunkId,
                DocumentId = documentId,
                ChunkIndex = chunkIndex,
                Content = content,
                Embedding = [],
                Document = document
            };

            results.Add((chunk, score));
        }

        return results;
    }

    public async Task<KnowledgeBaseDocument?> GetDocumentByHashAsync(string contentHash, CancellationToken ct = default)
    {
        return await _context.KnowledgeBaseDocuments
            .FirstOrDefaultAsync(d => d.ContentHash == contentHash, ct);
    }

    public async Task<KnowledgeBaseDocument?> GetDocumentBySourcePathAsync(string sourcePath, CancellationToken ct = default)
    {
        return await _context.KnowledgeBaseDocuments
            .FirstOrDefaultAsync(d => d.SourcePath == sourcePath, ct);
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        await _context.KnowledgeBaseDocuments
            .Where(d => d.Id == documentId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task UpdateDocumentSourcePathAsync(Guid documentId, string newSourcePath, CancellationToken ct = default)
    {
        await _context.KnowledgeBaseDocuments
            .Where(d => d.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SourcePath, newSourcePath), ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }
}
