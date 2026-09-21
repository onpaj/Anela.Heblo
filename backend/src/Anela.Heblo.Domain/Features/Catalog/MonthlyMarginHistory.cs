namespace Anela.Heblo.Domain.Features.Catalog;

public class MonthlyMarginHistory
{
    public Dictionary<DateTime, MarginData> MonthlyData { get; set; } = new();
    public DateTime LastUpdated { get; set; }

    // Calculated property - average across all months
    public MarginData Averages
    {
        get
        {
            if (MonthlyData.Count == 0)
                return new MarginData();

            return new MarginData
            {
                M0 = CalculateAverageMargin(m => m.M0),
                M1 = CalculateAverageMargin(m => m.M1),
                M2 = CalculateAverageMargin(m => m.M2),
                M3 = CalculateAverageMargin(m => m.M3)
            };
        }
    }

    private MarginLevel CalculateAverageMargin(Func<MarginData, MarginLevel> selector)
    {
        var margins = MonthlyData.Values.Select(selector).ToList();

        if (margins.Count == 0)
            return MarginLevel.Zero;

        var avgPercentage = margins.Average(m => m.Percentage);
        var avgAmount = margins.Average(m => m.Amount);
        var avgCostTotal = margins.Average(m => m.CostTotal);
        var avgCostLevel = margins.Average(m => m.CostLevel);

        return new MarginLevel(avgPercentage, avgAmount, avgCostTotal, avgCostLevel);
    }
}
