namespace Anela.Heblo.Domain.Features.Article;

public interface IArticleRepository
{
    Task AddAsync(Article article, CancellationToken ct = default);
    Task<Article?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(List<Article> Items, int TotalCount)> GetPagedAsync(ArticleStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<Article?> GetByIdForWriteAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Article> Items, int TotalCount)> GetArticlesPagedAsync(
        bool? hasFeedback, string? requestedBy, string sortBy, bool descending,
        int page, int pageSize, CancellationToken ct = default);
    Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
