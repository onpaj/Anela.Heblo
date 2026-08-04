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
        // Clamped here, not left to the [Range] attributes on ListArticlesRequest: the
        // controller manually constructs the request instead of binding it, so ASP.NET Core
        // model validation (and thus [Range]) never runs for this endpoint.
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedAsync(
            request.Status,
            page,
            pageSize,
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
            Page = page,
            PageSize = pageSize
        };
    }
}
