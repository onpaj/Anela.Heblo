using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public sealed class ListArticlesHandler : IRequestHandler<ListArticlesRequest, ListArticlesResponse>
{
    private readonly IArticleRepository _repository;

    public ListArticlesHandler(IArticleRepository repository)
    {
        _repository = repository;
    }

    public async Task<ListArticlesResponse> Handle(
        ListArticlesRequest request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Status,
            request.Page,
            request.PageSize,
            cancellationToken);

        return new ListArticlesResponse
        {
            Items = items.Select(a => new ArticleListItemDto
            {
                Id = a.Id,
                Topic = a.Topic,
                Title = a.Title,
                Status = a.Status,
                CreatedAt = a.CreatedAt,
                GeneratedAt = a.GeneratedAt
            }).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
