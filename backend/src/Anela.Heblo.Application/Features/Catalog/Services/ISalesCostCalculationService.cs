using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public interface ISalesCostCalculationService
{
    Task<Dictionary<string, List<SalesCost>>> CalculateSalesCostHistoryAsync(
        List<CatalogAggregate> products,
        CancellationToken cancellationToken = default);
}

public class SalesCost
{
    public DateTime Date { get; set; }
    public decimal MarketingCost { get; set; }
    public decimal StorageCost { get; set; }
    public decimal Total => MarketingCost + StorageCost;
}