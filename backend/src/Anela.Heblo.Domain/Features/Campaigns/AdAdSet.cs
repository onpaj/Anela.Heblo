namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdAdSet
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string PlatformAdSetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? DailyBudget { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AdCampaign Campaign { get; set; } = null!;
    public ICollection<Ad> Ads { get; set; } = new List<Ad>();
}
