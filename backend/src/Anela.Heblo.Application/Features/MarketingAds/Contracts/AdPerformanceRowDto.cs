namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>One entity's platform-reported metrics summed over the requested date range. Cost is net of VAT, account currency.</summary>
public class AdPerformanceRowDto
{
    public string EntityExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AdEntityStatus Status { get; set; }
    public string? ParentExternalId { get; set; }
    public string? ParentName { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public decimal? CtrPercent { get; set; }
    public decimal? AverageCpc { get; set; }
    public decimal? Roas { get; set; }
}
