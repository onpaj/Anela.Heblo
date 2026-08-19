namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned contract for pushing a completed ERP stock taking back into the catalog
/// caches, so the new stock level is visible immediately instead of only after the next
/// background data refresh. Implemented by the Catalog module via
/// CatalogManufactureStockSyncAdapter; DI registration lives in CatalogModule.
/// </summary>
public interface IManufactureCatalogStockSync
{
    /// <summary>
    /// Refreshes the cached catalog data of a single product after its ERP stock taking:
    /// writes the confirmed stock level and reloads the product's lots from the ERP.
    /// </summary>
    Task SyncErpStockTakingAsync(
        string productCode,
        decimal newStockAmount,
        CancellationToken cancellationToken = default);
}
