namespace Anela.Heblo.Domain.Features.Catalog.Sales;

public interface ICatalogSetPartsClient
{
    Task<IReadOnlyList<CatalogSetPart>> GetAsync(
        IEnumerable<string> setCodes,
        CancellationToken cancellationToken = default);
}
