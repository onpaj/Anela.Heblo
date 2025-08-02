namespace Anela.Heblo.Domain.Features.Catalog.Attributes;

public interface ICatalogAttributesClient
{
    Task<IList<CatalogAttributes>> GetAttributesAsync(int limit = 0, CancellationToken cancellationToken = default);
}
