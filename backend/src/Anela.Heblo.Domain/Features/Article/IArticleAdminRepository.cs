using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Domain.Features.Article;

/// <summary>
/// Admin-only repository for one-off operations on the Articles table. Not for runtime use.
/// </summary>
public interface IArticleAdminRepository
{
    Task<List<DomainArticle>> ListWithRequestedByAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
