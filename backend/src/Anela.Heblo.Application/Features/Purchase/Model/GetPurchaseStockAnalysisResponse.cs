namespace Anela.Heblo.Application.Features.Purchase.Model;

public class GetPurchaseStockAnalysisResponse
{
    public List<StockAnalysisItemDto> Items { get; set; } = new();

    public int TotalCount { get; set; }

    public int PageNumber { get; set; }

    public int PageSize { get; set; }

    public StockAnalysisSummaryDto Summary { get; set; } = new();
}

public class StockAnalysisItemDto
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ProductType { get; set; } = string.Empty;

    public double AvailableStock { get; set; }

    public double MinStockLevel { get; set; }

    public double OptimalStockLevel { get; set; }

    public double ConsumptionInPeriod { get; set; }

    public double DailyConsumption { get; set; }

    public int? DaysUntilStockout { get; set; }

    public double StockEfficiencyPercentage { get; set; }

    public StockSeverity Severity { get; set; }

    public string MinimalOrderQuantity { get; set; } = string.Empty;

    public LastPurchaseInfoDto? LastPurchase { get; set; }

    public List<string> Suppliers { get; set; } = new();

    public double? RecommendedOrderQuantity { get; set; }

    public bool IsConfigured { get; set; }
}

public class LastPurchaseInfoDto
{
    public DateTime Date { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public double Amount { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }
}

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

public enum StockSeverity
{
    Critical,
    Low,
    Optimal,
    Overstocked,
    NotConfigured
}