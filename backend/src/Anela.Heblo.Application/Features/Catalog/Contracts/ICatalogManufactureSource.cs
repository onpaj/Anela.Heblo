using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Manufacture planned-quantities, production history,
/// and manufactured-inventory totals. Implemented by the Manufacture module via an adapter.
///
/// NOTE: Returns Domain.Features.Manufacture.ManufactureHistoryRecord — a deliberate
/// pragmatic leak because this type is already woven through Catalog's CachedManufactureHistoryData
/// and CatalogAggregate.ManufactureHistory. The leak is allowlisted in ModuleBoundariesTests.
/// Tracked follow-up: introduce a Catalog-owned CatalogManufactureHistoryRecord DTO.
/// </summary>
public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
