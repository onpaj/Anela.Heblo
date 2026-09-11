namespace Anela.Heblo.Domain.Features.Campaigns;

public interface IGoogleAdsClient
{
    Task<IReadOnlyList<GoogleCampaignDto>> GetCampaignsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GoogleAdGroupDto>> GetAdGroupsAsync(string campaignId, CancellationToken ct = default);
    Task<IReadOnlyList<GoogleAdDto>> GetAdsAsync(string adGroupId, CancellationToken ct = default);
    Task<IReadOnlyList<GoogleMetricDto>> GetMetricsAsync(string adId, DateTime since, DateTime until, CancellationToken ct = default);
}

public class GoogleCampaignDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Objective { get; set; }
    public decimal? DailyBudget { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class GoogleAdGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? CpcBidMicros { get; set; }
}

public class GoogleAdDto
{
    public string Id { get; set; } = string.Empty;
    public string AdGroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class GoogleMetricDto
{
    public string AdId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal CostMicros { get; set; }
    public decimal ConversionsValue { get; set; }
    public long Conversions { get; set; }
}
