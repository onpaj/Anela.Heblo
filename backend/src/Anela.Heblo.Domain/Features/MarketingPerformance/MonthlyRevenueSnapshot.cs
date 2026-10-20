namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public class MonthlyRevenueSnapshot
{
    public int RetailOrderCount { get; init; }
    public decimal RetailRevenueWithVat { get; init; }
    public int WholesaleOrderCount { get; init; }
    public decimal WholesaleRevenueWithVat { get; init; }
    public int SkippedEurInvoiceCount { get; init; }
}
