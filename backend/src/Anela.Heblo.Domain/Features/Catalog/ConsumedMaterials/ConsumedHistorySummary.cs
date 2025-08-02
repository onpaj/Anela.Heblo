namespace Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;

public class ConsumedHistorySummary
{
    public Dictionary<string, MonthlyConsumedSummary> MonthlyData { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class MonthlyConsumedSummary
{
    public int Year { get; set; }
    public int Month { get; set; }
    public double TotalAmount { get; set; }
    public int ConsumptionCount { get; set; }

    public string MonthKey => $"{Year:D4}-{Month:D2}";
    public double AverageConsumption => ConsumptionCount > 0 ? TotalAmount / ConsumptionCount : 0;
}