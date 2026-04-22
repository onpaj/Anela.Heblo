namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class AdSetDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Status { get; set; }
    public IReadOnlyList<AdSummaryDto> Ads { get; set; } = [];
}
