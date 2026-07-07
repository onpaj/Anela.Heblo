namespace Anela.Heblo.Domain.Features.Analytics;

public static class AnalyticsProductExtensions
{
    public static bool HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)
        => product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
}
