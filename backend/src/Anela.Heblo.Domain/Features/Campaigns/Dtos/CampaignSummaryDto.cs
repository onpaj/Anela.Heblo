namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class CampaignSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public string? Status { get; set; }
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public long Conversions { get; set; }
    public decimal Roas { get; set; }
}
