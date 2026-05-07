using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticle;

public class GetArticleRequest : IRequest<GetArticleResponse>
{
    public Guid Id { get; set; }
}
