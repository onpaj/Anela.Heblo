namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

public class AdSearchTermRowDto
{
    public string SearchTerm { get; set; } = string.Empty;
    public KeywordMatchType? MatchType { get; set; }
    public string AdGroupExternalId { get; set; } = string.Empty;
    public string AdGroupName { get; set; } = string.Empty;
    public string? CampaignExternalId { get; set; }
    public string? CampaignName { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Cost { get; set; }
    public decimal Conversions { get; set; }
    public decimal ConversionValue { get; set; }
}
