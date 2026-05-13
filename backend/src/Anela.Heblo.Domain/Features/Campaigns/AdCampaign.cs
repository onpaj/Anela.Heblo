namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdCampaign
{
    public Guid Id { get; set; }
    public AdPlatform Platform { get; set; }
    public string PlatformCampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Objective { get; set; }
    public decimal? DailyBudget { get; set; }
    public decimal? LifetimeBudget { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<AdAdSet> AdSets { get; set; } = new List<AdAdSet>();
}
