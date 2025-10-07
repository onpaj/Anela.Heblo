using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface IManufactureCostCalculationService
{
    Task<Dictionary<string, List<ManufactureCost>>> CalculateManufactureCostHistoryAsync(
        List<CatalogAggregate> products,
        CancellationToken cancellationToken = default);

    bool IsLoaded { get; }

    Task Reload(List<CatalogAggregate> products);
}