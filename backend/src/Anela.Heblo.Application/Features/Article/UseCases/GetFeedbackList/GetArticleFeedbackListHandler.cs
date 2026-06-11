using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;

public sealed class GetArticleFeedbackListHandler
    : IRequestHandler<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly IArticleRepository _repository;
    private readonly IUserDisplayNameResolver _userDisplayNameResolver;

    public GetArticleFeedbackListHandler(
        IArticleRepository repository,
        IUserDisplayNameResolver userDisplayNameResolver)
    {
        _repository = repository;
        _userDisplayNameResolver = userDisplayNameResolver;
    }

    public async Task<GetArticleFeedbackListResponse> Handle(
        GetArticleFeedbackListRequest request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        // Queries run sequentially: they share the scoped DbContext, which EF Core
        // forbids issuing concurrent operations on (a Task.WhenAll here throws
        // "A second operation was started on this context instance").
        var (items, totalCount) = await _repository.GetFeedbackPagedAsync(
            request.HasFeedback,
            request.RequestedBy,
            sortBy,
            request.SortDescending,
            page,
            pageSize,
            ct);

        var stats = await _repository.GetFeedbackStatsAsync(ct);

        var userNames = await _userDisplayNameResolver.ResolveAsync(
            items.Select(a => a.RequestedBy).Where(id => id is not null)!,
            ct);

        return new GetArticleFeedbackListResponse
        {
            Items = items.Select(a => new ArticleFeedbackSummary
            {
                Id = a.Id,
                Title = a.Title,
                Topic = a.Topic,
                RequestedBy = a.RequestedBy,
                UserName = a.RequestedBy is not null ? userNames.GetValueOrDefault(a.RequestedBy) : null,
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
