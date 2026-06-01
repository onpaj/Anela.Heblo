using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;

public sealed class GetArticleFeedbackListHandler
    : IRequestHandler<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly IArticleRepository _repository;

    public GetArticleFeedbackListHandler(IArticleRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetArticleFeedbackListResponse> Handle(
        GetArticleFeedbackListRequest request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        var pagedTask = _repository.GetFeedbackPagedAsync(
            request.HasFeedback,
            request.RequestedBy,
            sortBy,
            request.SortDescending,
            page,
            pageSize,
            ct);
        var statsTask = _repository.GetFeedbackStatsAsync(ct);

        await Task.WhenAll(pagedTask, statsTask);

        var (items, totalCount) = pagedTask.Result;
        var stats = statsTask.Result;

        return new GetArticleFeedbackListResponse
        {
            Items = items.Select(a => new ArticleFeedbackSummary
            {
                Id = a.Id,
                Title = a.Title,
                Topic = a.Topic,
                RequestedBy = a.RequestedBy,
                CreatedAt = a.CreatedAt,
                PrecisionScore = a.PrecisionScore,
                StyleScore = a.StyleScore,
                HasComment = !string.IsNullOrWhiteSpace(a.FeedbackComment),
            }).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Stats = new ArticleFeedbackStatsDto
            {
                TotalArticles = stats.TotalArticles,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore is { } p ? Math.Round(p, 1) : null,
                AvgStyleScore = stats.AvgStyleScore is { } s ? Math.Round(s, 1) : null,
            },
        };
    }
}
