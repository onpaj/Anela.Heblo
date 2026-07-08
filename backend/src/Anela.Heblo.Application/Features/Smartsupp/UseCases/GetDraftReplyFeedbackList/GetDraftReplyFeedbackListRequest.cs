using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Rag;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetDraftReplyFeedbackList;

public class GetDraftReplyFeedbackListRequest : IRequest<GetDraftReplyFeedbackListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public bool? HasFeedback { get; set; }
    public string? UserId { get; set; }
}

public class GetDraftReplyFeedbackListResponse : BaseResponse
{
    public List<RagFeedbackLogSummary> Logs { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public RagFeedbackStatsDto Stats { get; set; } = new();
}
