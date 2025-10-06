namespace Anela.Heblo.Domain.Features.Catalog;

public class ProductMarginResult
{
    public MarginLevel M0 { get; set; }  // Material only
    public MarginLevel M1 { get; set; }  // + Manufacturing
    public MarginLevel M2 { get; set; }  // + Sales costs  
    public MarginLevel M3 { get; set; }  // + All overhead
    public CostBreakdown CostBreakdown { get; set; }

    // 12-month averages
    public MarginLevel M0Average12Months { get; set; }
    public MarginLevel M1Average12Months { get; set; }
    public MarginLevel M2Average12Months { get; set; }
    public MarginLevel M3Average12Months { get; set; }

    public ProductMarginResult(
        MarginLevel m0, MarginLevel m1, MarginLevel m2, MarginLevel m3,
        CostBreakdown costBreakdown,
        MarginLevel m0Average12Months, MarginLevel m1Average12Months,
        MarginLevel m2Average12Months, MarginLevel m3Average12Months)
    {
        M0 = m0;
        M1 = m1;
        M2 = m2;
        M3 = m3;
        CostBreakdown = costBreakdown;
        M0Average12Months = m0Average12Months;
        M1Average12Months = m1Average12Months;
        M2Average12Months = m2Average12Months;
        M3Average12Months = m3Average12Months;
    }

    public static ProductMarginResult Zero => new ProductMarginResult(
        MarginLevel.Zero, MarginLevel.Zero, MarginLevel.Zero, MarginLevel.Zero,
        CostBreakdown.Zero,
        MarginLevel.Zero, MarginLevel.Zero, MarginLevel.Zero, MarginLevel.Zero
    );
}