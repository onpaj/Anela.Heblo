using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns Domain.Features.Manufacture.ManufactureHistoryRecord — deliberate pragmatic exception;
    /// allowlisted in ModuleBoundariesTests. Track full decoupling as follow-up.
    /// </summary>
    Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
