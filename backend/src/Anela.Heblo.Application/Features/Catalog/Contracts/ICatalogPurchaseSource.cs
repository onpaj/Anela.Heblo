namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Purchase ordered-quantity totals.
/// Implemented by the Purchase module via an adapter.
/// </summary>
public interface ICatalogPurchaseSource
{
    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken);
}
