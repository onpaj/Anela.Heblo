namespace Anela.Heblo.Domain.Features.Article;

public class Article
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = "";
    public string Scope { get; set; } = "overview";
    public string? Audience { get; set; }
    public string? Angle { get; set; }
    public string Length { get; set; } = "medium (1000w)";
    public string? LanguageNote { get; set; }
    public bool UsedKnowledgeBase { get; set; }
    public bool UsedWebSearch { get; set; }
    public string? StyleGuideDriveId { get; set; }
    public string? StyleGuideItemPath { get; set; }
    public string? Title { get; set; }
    public string? HtmlContent { get; set; }
    public ArticleStatus Status { get; set; } = ArticleStatus.Queued;
    public string? ErrorMessage { get; set; }
    public string? RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public List<ArticleSource> Sources { get; set; } = new();
}
