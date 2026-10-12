using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdSearchTerms;

public class GetAdSearchTermsRequest : IRequest<GetAdSearchTermsResponse>
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    public string? CampaignExternalId { get; set; }
    public string? AdGroupExternalId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public decimal? MinCost { get; set; }
    public int Top { get; set; } = 200;
}
