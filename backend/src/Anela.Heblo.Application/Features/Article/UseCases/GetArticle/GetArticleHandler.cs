using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticle;

public sealed class GetArticleHandler : IRequestHandler<GetArticleRequest, GetArticleResponse>
{
    private readonly IArticleRepository _repository;

    public GetArticleHandler(IArticleRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetArticleResponse> Handle(
        GetArticleRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
            return new GetArticleResponse(ErrorCodes.ResourceNotFound, new Dictionary<string, string> { { "id", "empty" } });

        var article = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (article == null)
        {
            // TODO: replace with ErrorCodes.ArticleNotFound once Task 9 adds the 24XX block
            return new GetArticleResponse(ErrorCodes.ResourceNotFound, new Dictionary<string, string>
            {
                { "id", request.Id.ToString() }
            });
        }

        return new GetArticleResponse
        {
            Id = article.Id,
            Topic = article.Topic,
            Scope = article.Scope,
            Audience = article.Audience,
            Angle = article.Angle,
            Length = article.Length,
            Title = article.Title,
            HtmlContent = article.HtmlContent,
            Status = article.Status.ToString(),
            ErrorMessage = article.ErrorMessage,
            CreatedAt = article.CreatedAt,
            GeneratedAt = article.GeneratedAt,
            UseKnowledgeBase = article.UsedKnowledgeBase,
            UseWebSearch = article.UsedWebSearch,
            Sources = article.Sources.Select(s => new ArticleSourceDto
            {
                Title = s.Title,
                Url = s.Url,
                Type = s.Type.ToString()
            }).ToList()
        };
    }
}
