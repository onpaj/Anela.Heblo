namespace Anela.Heblo.Domain.Features.Article;

public sealed class ArticleSource
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public SourceType Type { get; set; }
    public double? Confidence { get; set; }
    public Guid? KnowledgeBaseChunkId { get; set; }
    public string? Excerpt { get; set; }
    public string? ValidationNote { get; set; }
}
