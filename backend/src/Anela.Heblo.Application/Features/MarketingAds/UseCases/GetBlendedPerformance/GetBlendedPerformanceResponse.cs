using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingAds.UseCases.GetBlendedPerformance;

public class GetBlendedPerformanceResponse : BaseResponse
{
    public BlendedGranularity Granularity { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AdBlendedPeriodDto> Periods { get; set; } = new();
    public AdBlendedPeriodDto Total { get; set; } = new();

    public GetBlendedPerformanceResponse() { }
    public GetBlendedPerformanceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
