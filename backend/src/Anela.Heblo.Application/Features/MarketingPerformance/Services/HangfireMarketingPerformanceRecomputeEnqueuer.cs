using Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class HangfireMarketingPerformanceRecomputeEnqueuer : IMarketingPerformanceRecomputeEnqueuer
{
    private readonly IBackgroundJobClient _client;
    private readonly ILogger<HangfireMarketingPerformanceRecomputeEnqueuer> _logger;

    public HangfireMarketingPerformanceRecomputeEnqueuer(IBackgroundJobClient client, ILogger<HangfireMarketingPerformanceRecomputeEnqueuer> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string? Enqueue(YearMonth from, YearMonth to)
    {
        try
        {
            return _client.Enqueue<MarketingPerformanceRecomputeJob>(j => j.RunAsync(from.Year, from.Month, to.Year, to.Month, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue marketing performance recompute {From}..{To}", from, to);
            return null;
        }
    }
}
