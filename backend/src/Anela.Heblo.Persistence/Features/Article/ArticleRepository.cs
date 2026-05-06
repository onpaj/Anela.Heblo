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

    public async Task<DomainArticle?> GetByIdForWriteAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Articles
            .Include(a => a.Sources)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<(IReadOnlyList<DomainArticle> Items, int TotalCount)> GetArticlesPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Articles.AsNoTracking().AsQueryable();

        if (hasFeedback == true)
            query = query.Where(a => a.PrecisionScore != null || a.StyleScore != null);
        else if (hasFeedback == false)
            query = query.Where(a => a.PrecisionScore == null && a.StyleScore == null);

        if (!string.IsNullOrWhiteSpace(requestedBy))
            query = query.Where(a => a.RequestedBy == requestedBy);

        query = (sortBy, descending) switch
        {
            ("PrecisionScore", true)  => query.OrderByDescending(a => a.PrecisionScore),
            ("PrecisionScore", false) => query.OrderBy(a => a.PrecisionScore),
            ("StyleScore", true)      => query.OrderByDescending(a => a.StyleScore),
            ("StyleScore", false)     => query.OrderBy(a => a.StyleScore),
            (_, true)                 => query.OrderByDescending(a => a.CreatedAt),
            _                         => query.OrderBy(a => a.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct = default)
    {
        var total = await _context.Articles.CountAsync(ct);
        var withFeedback = await _context.Articles
            .CountAsync(a => a.PrecisionScore != null || a.StyleScore != null, ct);
        var avgPrecision = await _context.Articles
            .Where(a => a.PrecisionScore != null)
            .AverageAsync(a => (double?)a.PrecisionScore, ct);
        var avgStyle = await _context.Articles
            .Where(a => a.StyleScore != null)
            .AverageAsync(a => (double?)a.StyleScore, ct);
        return new ArticleFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
