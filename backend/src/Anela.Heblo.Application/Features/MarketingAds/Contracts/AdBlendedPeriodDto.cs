namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>Real revenue (Shoptet, without VAT, non-cancelled, CZK) against total CZK ad cost at campaign level.</summary>
public class AdBlendedPeriodDto
{
    public DateOnly PeriodStart { get; set; }
    /// <summary>The period extends outside the requested range (a month cut by From/To).</summary>
    public bool IsPartial { get; set; }
    public decimal AdCostCzk { get; set; }
    public decimal GoogleAdsCostCzk { get; set; }
    public decimal MetaAdsCostCzk { get; set; }
    public decimal SklikCostCzk { get; set; }
    /// <summary>Some ad spend was in a non-CZK account and is NOT included in AdCostCzk.</summary>
    public bool HasNonCzkAdCost { get; set; }
    /// <summary>Days in the period that have synced ad cost; fewer than the period's days means cost is partial and ROAS overstated.</summary>
    public int DaysWithAdCost { get; set; }
    public int Orders { get; set; }
    public decimal RevenueWithoutVatCzk { get; set; }
    public decimal EshopRevenueWithoutVatCzk { get; set; }
    public decimal? BlendedRoas { get; set; }
    public decimal? PnoPercent { get; set; }
}
