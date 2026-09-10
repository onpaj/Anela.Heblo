using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public class ListArticlesRequest : IRequest<ListArticlesResponse>
{
    public ArticleStatus? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
