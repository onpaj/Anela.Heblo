using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetFeedbackList;

public class GetFeedbackListHandler : IRequestHandler<GetFeedbackListRequest, GetFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly IKnowledgeBaseRepository _repository;

    public GetFeedbackListHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetFeedbackListResponse> Handle(
        GetFeedbackListRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        var (logs, totalCount) = await _repository.GetFeedbackLogsPagedAsync(
            request.HasFeedback,
            request.UserId,
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);

        var stats = await _repository.GetFeedbackStatsAsync(cancellationToken);

        return new GetFeedbackListResponse
        {
            Logs = logs.Select(l => new FeedbackLogSummary
            {
                Id = l.Id,
                Question = l.Question,
                Answer = l.Answer,
                TopK = l.TopK,
                SourceCount = l.SourceCount,
                DurationMs = l.DurationMs,
                CreatedAt = l.CreatedAt,
                UserId = l.UserId,
                PrecisionScore = l.PrecisionScore,
                StyleScore = l.StyleScore,
                FeedbackComment = l.FeedbackComment,
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Stats = new FeedbackStatsDto
            {
                TotalQuestions = stats.TotalQuestions,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore,
                AvgStyleScore = stats.AvgStyleScore,
            },
        };
    }
}
