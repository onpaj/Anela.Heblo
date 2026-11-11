namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class StockAnalysisSummaryDto
{
    public int TotalProducts { get; set; }

    public int CriticalCount { get; set; }

    public int LowStockCount { get; set; }

    public int OptimalCount { get; set; }

    public int OverstockedCount { get; set; }

    public int NotConfiguredCount { get; set; }

    public decimal TotalInventoryValue { get; set; }

    public DateTime AnalysisPeriodStart { get; set; }

    public DateTime AnalysisPeriodEnd { get; set; }
}
