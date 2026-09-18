using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;

/// <summary>Fire-and-forget Hangfire job behind POST /api/marketing-performance/recompute. Ignores month locks.</summary>
public class MarketingPerformanceRecomputeJob
{
    private readonly IMarketingPerformanceRefreshService _service;
    private readonly MarketingPerformanceRunGuard _guard;
    private readonly ILogger<MarketingPerformanceRecomputeJob> _logger;

    public MarketingPerformanceRecomputeJob(IMarketingPerformanceRefreshService service, MarketingPerformanceRunGuard guard, ILogger<MarketingPerformanceRecomputeJob> logger)
    {
        _service = service;
        _guard = guard;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken cancellationToken)
    {
        var from = new YearMonth(fromYear, fromMonth);
        var to = new YearMonth(toYear, toMonth);

        if (!_guard.TryBegin())
        {
            _logger.LogWarning("Marketing performance recompute {From}..{To} skipped: another run is active", from, to);
            return;
        }

        try
        {
            var result = await _service.RecomputeRangeAsync(from, to, cancellationToken);
            _logger.LogInformation("Marketing performance recompute {From}..{To}: {Ok}/{Total} months fully refreshed",
                from, to, result.Months.Count(m => m.RevenueOk && m.CostsOk), result.Months.Count);
        }
        finally
        {
            _guard.End();
        }
    }
}
