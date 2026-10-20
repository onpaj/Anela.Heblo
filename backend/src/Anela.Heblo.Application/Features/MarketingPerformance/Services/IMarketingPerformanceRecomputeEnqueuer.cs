using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public interface IMarketingPerformanceRecomputeEnqueuer
{
    /// <summary>Enqueues the recompute; returns the Hangfire job id or null when enqueueing failed.</summary>
    string? Enqueue(YearMonth from, YearMonth to);
}
