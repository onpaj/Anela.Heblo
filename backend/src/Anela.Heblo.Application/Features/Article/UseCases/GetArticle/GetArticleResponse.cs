using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticle;

public sealed class GetArticleResponse : BaseResponse
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string? Audience { get; set; }
    public string? Angle { get; set; }
    public string Length { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? HtmlContent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public bool UseKnowledgeBase { get; set; }
    public bool UseWebSearch { get; set; }
    public List<ArticleSourceDto> Sources { get; set; } = [];

    public GetArticleResponse() { }

    public GetArticleResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
