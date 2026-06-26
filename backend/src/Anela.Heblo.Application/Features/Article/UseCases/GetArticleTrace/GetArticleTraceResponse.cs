using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;

public sealed class GetArticleTraceResponse : BaseResponse
{
    public Guid ArticleId { get; set; }
    public List<ArticleGenerationStepDto> Steps { get; set; } = [];

    public GetArticleTraceResponse() { }

    public GetArticleTraceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}

public sealed class ArticleGenerationStepDto
{
    public Guid Id { get; set; }
    public string StepName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public ArticleGenerationStepStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Model { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
}
