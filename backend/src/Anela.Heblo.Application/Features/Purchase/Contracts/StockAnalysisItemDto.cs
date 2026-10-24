namespace Anela.Heblo.Application.Features.Purchase.Contracts;

public class StockAnalysisItemDto
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ProductNameNormalized { get; set; } = string.Empty;

    public string? ProductNameSuffix { get; set; }

    public string ProductType { get; set; } = string.Empty;

    public double AvailableStock { get; set; }

    public double OrderedStock { get; set; }

    public double EffectiveStock { get; set; }

    public double MinStockLevel { get; set; }

    public double OptimalStockLevel { get; set; }

    public double ConsumptionInPeriod { get; set; }

    public double DailyConsumption { get; set; }

    public int? DaysUntilStockout { get; set; }

    public double StockEfficiencyPercentage { get; set; }

    public StockSeverity Severity { get; set; }

    public string MinimalOrderQuantity { get; set; } = string.Empty;

    public LastPurchaseInfoDto? LastPurchase { get; set; }

    public string? Supplier { get; set; }

    public double? RecommendedOrderQuantity { get; set; }

    public bool IsConfigured { get; set; }
}
