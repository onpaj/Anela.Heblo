using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleFeedbackList;

public class GetArticleFeedbackListHandler : IRequestHandler<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>
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
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        var articlesTask = _repository.GetArticlesPagedAsync(
            request.HasFeedback,
            request.RequestedBy,
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);

        var statsTask = _repository.GetFeedbackStatsAsync(cancellationToken);

        await Task.WhenAll(articlesTask, statsTask);

        var (articles, totalCount) = await articlesTask;
        var stats = await statsTask;

        return new GetArticleFeedbackListResponse
        {
            Articles = articles.Select(a => new ArticleFeedbackSummary
            {
                Id = a.Id,
                Topic = a.Topic,
                Title = a.Title,
                Status = a.Status.ToString(),
                CreatedAt = a.CreatedAt,
                GeneratedAt = a.GeneratedAt,
                RequestedBy = a.RequestedBy,
                PrecisionScore = a.PrecisionScore,
                StyleScore = a.StyleScore,
                FeedbackComment = a.FeedbackComment,
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Stats = new ArticleFeedbackStatsDto
            {
                TotalArticles = stats.TotalArticles,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore,
                AvgStyleScore = stats.AvgStyleScore,
            },
        };
    }
}
