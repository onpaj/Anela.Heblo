using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;

public class GetBlendedPerformanceRequest : IRequest<GetBlendedPerformanceResponse>
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public BlendedGranularity Granularity { get; set; } = BlendedGranularity.Monthly;
}
