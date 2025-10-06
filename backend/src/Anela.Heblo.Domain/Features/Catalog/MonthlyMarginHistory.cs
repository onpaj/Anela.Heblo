namespace Anela.Heblo.Domain.Features.Catalog;

public class MonthlyMarginHistory
{
    public List<MonthlyMarginData> MonthlyData { get; set; } = new();
    public MarginAverages YearlyAverages { get; set; } = new();
}

public class MonthlyMarginData
{
    public DateTime Month { get; set; }
    public MarginLevel M0 { get; set; } = MarginLevel.Zero;
    public MarginLevel M1 { get; set; } = MarginLevel.Zero;
    public MarginLevel M2 { get; set; } = MarginLevel.Zero;
    public MarginLevel M3 { get; set; } = MarginLevel.Zero;
    public CostBreakdown CostsForMonth { get; set; } = CostBreakdown.Zero;
}

public class MarginAverages
{
    public MarginLevel M0Average { get; set; } = MarginLevel.Zero;
    public MarginLevel M1Average { get; set; } = MarginLevel.Zero;
    public MarginLevel M2Average { get; set; } = MarginLevel.Zero;
    public MarginLevel M3Average { get; set; } = MarginLevel.Zero;
}