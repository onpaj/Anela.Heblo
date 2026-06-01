namespace Anela.Heblo.Domain.Features.Article;

public interface IArticleRepository
{
    Task AddAsync(Article article, CancellationToken ct = default);
    Task<Article?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Article?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<(List<Article> Items, int TotalCount)> GetPagedAsync(ArticleStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct = default);
    Task<Article?> GetWithStepsAsync(Guid id, CancellationToken ct = default);
    Task AddStepAsync(ArticleGenerationStep step, CancellationToken ct = default);
    Task UpdateStepAsync(ArticleGenerationStep step, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
