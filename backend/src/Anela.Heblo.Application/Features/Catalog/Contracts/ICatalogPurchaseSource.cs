namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public interface ICatalogPurchaseSource
{
    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken);
}
