using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;

public class GetLeafletFeedbackListRequest : IRequest<GetLeafletFeedbackListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public bool? HasFeedback { get; set; }
    public string? UserId { get; set; }
}

public class GetLeafletFeedbackListResponse : BaseResponse
{
    public List<LeafletFeedbackSummary> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public LeafletFeedbackStatsDto Stats { get; set; } = new();
}

public class LeafletFeedbackSummary
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public string FinalMarkdown { get; set; } = string.Empty;
    public int KbSourceCount { get; set; }
    public int LeafletSourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public bool HasFeedback => PrecisionScore.HasValue || StyleScore.HasValue;
}

public class LeafletFeedbackStatsDto
{
    public int TotalGenerations { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
