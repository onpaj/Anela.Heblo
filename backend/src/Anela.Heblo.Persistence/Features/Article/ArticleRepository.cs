using Microsoft.EntityFrameworkCore;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;
using DomainArticleStatus = Anela.Heblo.Domain.Features.Article.ArticleStatus;
using DomainIArticleRepository = Anela.Heblo.Domain.Features.Article.IArticleRepository;

namespace Anela.Heblo.Persistence.Features.Article;

public class ArticleRepository : DomainIArticleRepository
{
    private readonly ApplicationDbContext _context;

    public ArticleRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task AddAsync(DomainArticle article, CancellationToken ct = default)
    {
        _context.Articles.Add(article);
        return Task.CompletedTask;
    }

    public async Task<DomainArticle?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Articles
            .Include(a => a.Sources)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<(List<DomainArticle> Items, int TotalCount)> GetPagedAsync(
        DomainArticleStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Articles.AsQueryable();

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

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _context.SaveChangesAsync(ct);
    }
}
