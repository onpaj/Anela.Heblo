using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public class ListArticlesRequest : IRequest<ListArticlesResponse>
{
    public ArticleStatus? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
