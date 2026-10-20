using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;

public class RecomputeMarketingPerformanceResponse : BaseResponse
{
    public string? JobId { get; set; }
    public int MonthCount { get; set; }

    public RecomputeMarketingPerformanceResponse() { }
    public RecomputeMarketingPerformanceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
