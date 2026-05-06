using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;

public class GetArticleFeedbackListRequest : IRequest<GetArticleFeedbackListResponse>
{
    public bool? HasFeedback { get; set; }
    public string? RequestedBy { get; set; }
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class GetArticleFeedbackListResponse : BaseResponse
{
    public List<ArticleFeedbackSummary> Items { get; set; } = [];
    public ArticleFeedbackStatsDto Stats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    public GetArticleFeedbackListResponse() { }

    public GetArticleFeedbackListResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
        : base(errorCode, details)
    {
    }
}

public class ArticleFeedbackSummary
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public bool HasComment { get; set; }
}

public class ArticleFeedbackStatsDto
{
    public int TotalArticles { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
