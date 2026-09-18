namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>Cost step of the snapshot: received invoices for one month whose supplier DIČ is in <paramref name="vatIds"/>.</summary>
public interface IMonthlyAdCostSource
{
    Task<IReadOnlyList<AdCostInvoice>> GetAsync(YearMonth month, IReadOnlyCollection<string> vatIds, CancellationToken cancellationToken);
}
