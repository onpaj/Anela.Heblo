using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;

public class GetLeafletFeedbackListHandler
    : IRequestHandler<GetLeafletFeedbackListRequest, GetLeafletFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly ILeafletGenerationRepository _repository;

    public GetLeafletFeedbackListHandler(ILeafletGenerationRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetLeafletFeedbackListResponse> Handle(
        GetLeafletFeedbackListRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        var (items, totalCount) = await _repository.GetGenerationsPagedAsync(
            request.HasFeedback, request.UserId, sortBy, request.SortDescending,
            pageNumber, pageSize, cancellationToken);

        var stats = await _repository.GetGenerationStatsAsync(cancellationToken);

        return new GetLeafletFeedbackListResponse
        {
            Items = items.Select(g => new LeafletFeedbackSummary
            {
                Id = g.Id,
                Topic = g.Topic,
                Audience = g.Audience,
                Length = g.Length,
                FinalMarkdown = g.FinalMarkdown,
                KbSourceCount = g.KbSourceCount,
                LeafletSourceCount = g.LeafletSourceCount,
                DurationMs = g.DurationMs,
                CreatedAt = g.CreatedAt,
                UserId = g.UserId,
                PrecisionScore = g.PrecisionScore,
                StyleScore = g.StyleScore,
                FeedbackComment = g.FeedbackComment,
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Stats = new LeafletFeedbackStatsDto
            {
                TotalGenerations = stats.TotalGenerations,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore,
                AvgStyleScore = stats.AvgStyleScore,
            },
        };
    }
}
