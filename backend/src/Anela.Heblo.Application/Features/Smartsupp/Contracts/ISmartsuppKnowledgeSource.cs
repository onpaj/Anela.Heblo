namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

/// <summary>
/// Smartsupp-owned read-only abstraction over the knowledge-base search index.
/// Implemented by the KnowledgeBase module via an adapter.
/// Structurally mirrors <c>IArticleKnowledgeSource</c> (string-query shape) — not
/// <c>ILeafletKnowledgeSource</c> (embedding-vector shape) — because
/// <c>GenerateDraftReplyHandler</c> builds a plain-text retrieval query and never
/// computes an embedding itself.
/// </summary>
public interface ISmartsuppKnowledgeSource
{
    Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}

public class SmartsuppKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string SourceFilename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
