using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleFeedbackList;

public class GetArticleFeedbackListRequest : IRequest<GetArticleFeedbackListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public bool? HasFeedback { get; set; }
    public string? RequestedBy { get; set; }
}

public class GetArticleFeedbackListResponse : BaseResponse
{
    public List<ArticleFeedbackSummary> Articles { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public ArticleFeedbackStatsDto Stats { get; set; } = new();
}

public class ArticleFeedbackSummary
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? RequestedBy { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public bool HasFeedback => PrecisionScore.HasValue || StyleScore.HasValue;
}

public class ArticleFeedbackStatsDto
{
    public int TotalArticles { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
