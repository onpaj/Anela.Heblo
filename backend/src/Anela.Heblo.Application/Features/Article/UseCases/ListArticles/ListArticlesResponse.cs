using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public class ListArticlesResponse : BaseResponse
{
    public List<ArticleListItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ArticleListItemDto
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
}
