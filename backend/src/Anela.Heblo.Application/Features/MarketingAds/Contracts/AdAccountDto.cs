namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public class AdAccountDto
{
    public AdPlatform Platform { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
}
