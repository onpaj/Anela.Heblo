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

            var avgM0 = CalculateAverageMargin(m => m.M0);
            var avgM1A = CalculateAverageMargin(m => m.M1_A);
            var avgM1B = CalculateAverageMargin(m => m.M1_B);
            var avgM2 = CalculateAverageMargin(m => m.M2);

            return new MarginData
            {
                M0 = avgM0,
                M1_A = avgM1A,
                M1_B = avgM1B,
                M2 = avgM2
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