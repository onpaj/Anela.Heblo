using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public sealed class ListArticlesResponse : BaseResponse
{
    public List<ArticleListItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public ListArticlesResponse() { }

    public ListArticlesResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
