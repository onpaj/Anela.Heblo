using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetAdPerformance;

public class GetAdPerformanceRequest : IRequest<GetAdPerformanceResponse>
{
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = string.Empty;
    /// <summary>Exactly one level is aggregated; facts exist at every level and must never be summed across levels.</summary>
    public AdEntityLevel Level { get; set; } = AdEntityLevel.Campaign;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    /// <summary>Only children of this campaign/ad group.</summary>
    public string? ParentExternalId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}
