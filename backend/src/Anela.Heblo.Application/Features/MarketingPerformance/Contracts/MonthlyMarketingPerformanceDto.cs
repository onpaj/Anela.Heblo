using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

/// <summary>One month of the "Analýzy" table. Sums come from the snapshot; ratios are derived here.</summary>
public class MonthlyMarketingPerformanceDto
{
    [Required] public int Year { get; set; }
    [Required] public int Month { get; set; }
    /// <summary>"MM/yyyy"</summary>
    [Required] public string MonthYearDisplay { get; set; } = string.Empty;
    /// <summary>False when the snapshot has no row for this month yet.</summary>
    [Required] public bool HasData { get; set; }
    [Required] public bool IsLocked { get; set; }
    /// <summary>True for the current calendar month (incomplete).</summary>
    [Required] public bool IsPartial { get; set; }

    [Required] public int Orders { get; set; }
    [Required] public decimal RevenueWithVat { get; set; }
    [Required] public decimal RevenueWithoutVat { get; set; }
    [Required] public List<ChannelCostDto> ChannelCosts { get; set; } = new();
    [Required] public decimal TotalCost { get; set; }
    /// <summary>Podíl nákladů na obratu, % of revenue without VAT. Null when revenue is 0.</summary>
    public decimal? Pno { get; set; }
    /// <summary>Return on ad spend, %. Null when cost is 0.</summary>
    public decimal? Roas { get; set; }
    /// <summary>Revenue without VAT minus total cost.</summary>
    [Required] public decimal Profit { get; set; }
    public decimal? AvgOrderValue { get; set; }
    public decimal? CostPerOrder { get; set; }
    public decimal? YoyCostPercent { get; set; }
    public decimal? YoyRevenuePercent { get; set; }
    public decimal? YoyOrdersPercent { get; set; }

    [Required] public int SkippedEurInvoiceCount { get; set; }
    public DateTime? RevenueComputedAt { get; set; }
    public DateTime? CostsComputedAt { get; set; }
    public string? LastError { get; set; }
}
