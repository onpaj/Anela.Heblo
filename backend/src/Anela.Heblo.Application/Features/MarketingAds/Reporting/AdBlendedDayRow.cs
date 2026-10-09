namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Row of ads.v_ads_blended_daily, materialised with Database.SqlQuery (property names = column aliases).</summary>
public class AdBlendedDayRow
{
    public DateOnly Date { get; set; }
    public decimal AdCostCzk { get; set; }
    public decimal GoogleAdsCostCzk { get; set; }
    public decimal MetaAdsCostCzk { get; set; }
    public decimal SklikCostCzk { get; set; }
    public bool HasNonCzkAdCost { get; set; }
    public int Orders { get; set; }
    public decimal RevenueWithoutVatCzk { get; set; }
    public decimal EshopRevenueWithoutVatCzk { get; set; }
}
