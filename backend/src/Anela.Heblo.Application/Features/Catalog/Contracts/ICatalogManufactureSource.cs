using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Manufacture planned-quantities, production history,
/// and manufactured-inventory totals. Implemented by the Manufacture module via an adapter.
/// </summary>
public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogManufactureRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
