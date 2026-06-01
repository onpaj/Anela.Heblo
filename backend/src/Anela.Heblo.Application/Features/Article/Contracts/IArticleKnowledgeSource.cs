namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Article-owned read-only abstraction over the knowledge-base search index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface IArticleKnowledgeSource
{
    Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}

public class ArticleKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public string SourceFilename { get; set; } = "";
    public string Content { get; set; } = "";
    public double Score { get; set; }
}
