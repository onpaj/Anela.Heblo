using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetFeedbackList;

public class GetFeedbackListRequest : IRequest<GetFeedbackListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public bool? HasFeedback { get; set; }
    public string? UserId { get; set; }
}

public class GetFeedbackListResponse : BaseResponse
{
    public List<FeedbackLogSummary> Logs { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public FeedbackStatsDto Stats { get; set; } = new();
}

public class FeedbackLogSummary
{
    public Guid Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int TopK { get; set; }
    public int SourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public bool HasFeedback => PrecisionScore.HasValue || StyleScore.HasValue;
}

public class FeedbackStatsDto
{
    public int TotalQuestions { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
