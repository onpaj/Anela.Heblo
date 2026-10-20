using MediatR;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;

public class RecomputeMarketingPerformanceRequest : IRequest<RecomputeMarketingPerformanceResponse>
{
    /// <summary>"yyyy-MM"</summary>
    public string From { get; set; } = string.Empty;
    /// <summary>"yyyy-MM"</summary>
    public string To { get; set; } = string.Empty;
}
