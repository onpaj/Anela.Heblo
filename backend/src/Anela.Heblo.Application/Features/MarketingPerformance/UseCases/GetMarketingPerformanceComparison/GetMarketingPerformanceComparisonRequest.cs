using MediatR;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;

public class GetMarketingPerformanceComparisonRequest : IRequest<GetMarketingPerformanceComparisonResponse>
{
    /// <summary>Number of calendar years ending with the current one. Clamped to 2..3.</summary>
    public int Years { get; set; } = 3;
    public bool IncludeWholesale { get; set; }
}
