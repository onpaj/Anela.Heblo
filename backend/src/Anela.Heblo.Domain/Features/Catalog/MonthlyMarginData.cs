namespace Anela.Heblo.Domain.Features.Catalog;

public class MonthlyMarginData
{
    public DateTime Month { get; init; }
    public MarginLevel M0 { get; init; } = MarginLevel.Zero;
    public MarginLevel M1_A { get; init; } = MarginLevel.Zero;  // Economic baseline (always present)
    public MarginLevel? M1_B { get; init; }  // Actual monthly cost (nullable - only when produced)
    public MarginLevel M2 { get; init; } = MarginLevel.Zero;
    public MarginLevel M3 { get; init; } = MarginLevel.Zero;
    public CostBreakdown CostsForMonth { get; init; } = CostBreakdown.Zero;
}