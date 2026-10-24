namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned read abstraction over Catalog products. Implemented by the Catalog
/// module via CatalogManufactureCatalogSourceAdapter. Returns the Manufacture-owned
/// ProductCatalogSnapshot, so Manufacture does not depend on Catalog domain types.
/// </summary>
public interface IManufactureCatalogSource
{
    Task<ProductCatalogSnapshot?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, ProductCatalogSnapshot>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ProductCatalogSnapshot>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Product code -> name suffix (Flexi cenik.popisC) for the given codes; codes without a suffix are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetProductNameSuffixesAsync(
        IEnumerable<string> productCodes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads planned manufacture quantities into the catalog cache. Call after a write that
    /// changes them, so consumers do not wait for the scheduled background refresh.
    /// </summary>
    Task RefreshPlannedDataAsync(CancellationToken cancellationToken = default);
}
