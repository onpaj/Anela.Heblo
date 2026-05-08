using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;

public class GetArticleTraceRequest : IRequest<GetArticleTraceResponse>
{
    public Guid Id { get; set; }
}
