using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;

public sealed class GetArticleTraceHandler : IRequestHandler<GetArticleTraceRequest, GetArticleTraceResponse>
{
    private readonly IArticleRepository _repository;

    public GetArticleTraceHandler(IArticleRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetArticleTraceResponse> Handle(
        GetArticleTraceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty)
            return new GetArticleTraceResponse(ErrorCodes.ArticleNotFound, new Dictionary<string, string> { { "id", "empty" } });

        var article = await _repository.GetWithStepsAsync(request.Id, cancellationToken);
        if (article == null)
        {
            return new GetArticleTraceResponse(ErrorCodes.ArticleNotFound, new Dictionary<string, string>
            {
                { "id", request.Id.ToString() }
            });
        }

        return new GetArticleTraceResponse
        {
            ArticleId = article.Id,
            Steps = article.Steps
                .OrderBy(s => s.Sequence)
                .Select(s => new ArticleGenerationStepDto
                {
                    Id = s.Id,
                    StepName = s.StepName,
                    Sequence = s.Sequence,
                    Status = s.Status,
                    StartedAt = s.StartedAt,
                    FinishedAt = s.FinishedAt,
                    DurationMs = s.DurationMs,
                    Model = s.Model,
                    InputJson = s.InputJson,
                    OutputJson = s.OutputJson,
                    ErrorMessage = s.ErrorMessage,
                })
                .ToList(),
        };
    }
}
