using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface ISalesCostCalculationService
{
    Task<Dictionary<string, List<SalesCost>>> CalculateSalesCostHistoryAsync(
        List<CatalogAggregate> products,
        CancellationToken cancellationToken = default);
}