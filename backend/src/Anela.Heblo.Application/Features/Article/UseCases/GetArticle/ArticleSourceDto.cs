namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticle;

public sealed class ArticleSourceDto
{
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid? KnowledgeBaseChunkId { get; set; }
    public double? Confidence { get; set; }
    public string? Excerpt { get; set; }
    public string? ValidationNote { get; set; }
}
