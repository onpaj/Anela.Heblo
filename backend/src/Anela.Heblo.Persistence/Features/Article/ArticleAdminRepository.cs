using Anela.Heblo.Domain.Features.Article;
using Microsoft.EntityFrameworkCore;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Persistence.Features.Article;

public sealed class ArticleAdminRepository : IArticleAdminRepository
{
    private readonly ApplicationDbContext _context;

    public ArticleAdminRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<List<DomainArticle>> ListWithRequestedByAsync(CancellationToken ct = default) =>
        _context.Articles
            .Where(a => a.RequestedBy != null)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _context.SaveChangesAsync(ct);
}
