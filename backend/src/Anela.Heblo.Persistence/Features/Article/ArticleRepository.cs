using Anela.Heblo.Domain.Features.Article;
using Microsoft.EntityFrameworkCore;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;
using DomainArticleStatus = Anela.Heblo.Domain.Features.Article.ArticleStatus;

namespace Anela.Heblo.Persistence.Features.Article;

public class ArticleRepository : IArticleRepository
{
    private readonly ApplicationDbContext _context;

    public ArticleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(DomainArticle article, CancellationToken ct = default)
    {
        _context.Articles.Add(article); // EF Add is synchronous; ct is not applicable here
        return Task.CompletedTask;
    }

    public async Task<DomainArticle?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Articles
            .AsNoTracking()
            .Include(a => a.Sources)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<(List<DomainArticle> Items, int TotalCount)> GetPagedAsync(
        DomainArticleStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Articles.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<DomainArticle?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Articles
            .Include(a => a.Sources)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Articles.AsNoTracking();

        if (hasFeedback == true)
            query = query.Where(a => a.PrecisionScore != null || a.StyleScore != null);
        else if (hasFeedback == false)
            query = query.Where(a => a.PrecisionScore == null && a.StyleScore == null);

        if (!string.IsNullOrWhiteSpace(requestedBy))
            query = query.Where(a => a.RequestedBy == requestedBy);

        query = sortBy switch
        {
            "PrecisionScore" => descending
                ? query.OrderByDescending(a => a.PrecisionScore)
                : query.OrderBy(a => a.PrecisionScore),
            "StyleScore" => descending
                ? query.OrderByDescending(a => a.StyleScore)
                : query.OrderBy(a => a.StyleScore),
            _ => descending
                ? query.OrderByDescending(a => a.CreatedAt)
                : query.OrderBy(a => a.CreatedAt)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ArticleFeedbackProjection(
                a.Id,
                a.Title,
                a.Topic,
                a.RequestedBy,
                a.CreatedAt,
                a.PrecisionScore,
                a.StyleScore,
                a.FeedbackComment))
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct = default)
    {
        var stats = await _context.Articles
            .GroupBy(_ => 1)
            .Select(g => new ArticleFeedbackStats(
                g.Count(),
                g.Count(a => a.PrecisionScore != null || a.StyleScore != null),
                g.Average(a => (double?)a.PrecisionScore),
                g.Average(a => (double?)a.StyleScore)))
            .FirstOrDefaultAsync(ct);

        return stats ?? new ArticleFeedbackStats(0, 0, null, null);
    }

    public async Task<DomainArticle?> GetWithStepsAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Articles
            .AsNoTracking()
            .Include(a => a.Steps.OrderBy(s => s.Sequence))
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public Task AddStepAsync(ArticleGenerationStep step, CancellationToken ct = default)
    {
        _context.ArticleGenerationSteps.Add(step);
        return Task.CompletedTask;
    }

    public Task UpdateStepAsync(ArticleGenerationStep step, CancellationToken ct = default)
    {
        _context.ArticleGenerationSteps.Update(step);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
