namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class CampaignDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public string? Status { get; set; }
    public IReadOnlyList<AdSetDetailDto> AdSets { get; set; } = [];
}
