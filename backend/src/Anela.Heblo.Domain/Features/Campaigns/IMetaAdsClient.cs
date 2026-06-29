namespace Anela.Heblo.Domain.Features.Campaigns;

public interface IMetaAdsClient
{
    Task<IReadOnlyList<MetaCampaignDto>> GetCampaignsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MetaAdSetDto>> GetAdSetsAsync(string campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<MetaAdDto>> GetAdsAsync(string adSetId, CancellationToken ct = default);
    Task<IReadOnlyList<MetaInsightDto>> GetInsightsAsync(string adId, DateTime since, DateTime until, CancellationToken ct = default);
}

public class MetaCampaignDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Objective { get; set; }
    public decimal? DailyBudget { get; set; }
    public decimal? LifetimeBudget { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? StopTime { get; set; }
}

public class MetaAdSetDto
{
    public string Id { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? DailyBudget { get; set; }
}

public class MetaAdDto
{
    public string Id { get; set; } = string.Empty;
    public string AdSetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class MetaInsightDto
{
    public string AdId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Spend { get; set; }
    public decimal Revenue { get; set; }
    public long Conversions { get; set; }
}
