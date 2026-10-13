namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>Revenue step of the snapshot: local IssuedInvoices aggregated for one month.</summary>
public interface IMonthlyRevenueSource
{
    Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken);
}
