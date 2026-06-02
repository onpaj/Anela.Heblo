namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Logistics transport-box state.
/// Implemented by the Logistics module via an adapter.
/// Returns productCode → summed item amount dictionaries.
/// </summary>
public interface ICatalogTransportSource
{
    Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken);

    Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken);

    Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken);
}
