using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned read abstraction over Catalog products. Implemented by the Catalog
/// module via CatalogManufactureCatalogSourceAdapter. Returns CatalogAggregate as a
/// deliberate pragmatic leak — symmetric to ICatalogManufactureSource returning
/// ManufactureHistoryRecord. Allowlisted in ModuleBoundariesTests under
/// "Manufacture -> Catalog". Follow-up: introduce Manufacture-owned ProductCatalogSnapshot DTO.
/// </summary>
public interface IManufactureCatalogSource
{
    Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
