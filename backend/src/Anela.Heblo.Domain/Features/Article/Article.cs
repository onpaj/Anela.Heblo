namespace Anela.Heblo.Domain.Features.Article;

public sealed class Article
{
    public const string DefaultScope = "overview";
    public const string DefaultLength = "medium (1000w)";

    public Guid Id { get; set; }
    public string Topic { get; set; } = "";
    public string Scope { get; set; } = DefaultScope;
    public string? Audience { get; set; }
    public string? Angle { get; set; }
    public string Length { get; set; } = DefaultLength;
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
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public List<ArticleSource> Sources { get; set; } = new();
    public List<ArticleGenerationStep> Steps { get; set; } = new();

    public void MarkAsResearching() => Status = ArticleStatus.Researching;

    public void MarkAsWriting() => Status = ArticleStatus.Writing;

    public void MarkAsGenerated(string? title, string? htmlContent)
    {
        Title = title;
        HtmlContent = htmlContent;
        Status = ArticleStatus.Generated;
        GeneratedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsFailed(string errorMessage)
    {
        Status = ArticleStatus.Failed;
        ErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage;
    }

    public void SubmitFeedback(int precisionScore, int styleScore, string? comment)
    {
        PrecisionScore = precisionScore;
        StyleScore = styleScore;
        FeedbackComment = comment;
    }
}
