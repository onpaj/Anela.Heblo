using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;

public class RecomputeMarketingPerformanceHandler : IRequestHandler<RecomputeMarketingPerformanceRequest, RecomputeMarketingPerformanceResponse>
{
    private readonly IMarketingPerformanceRecomputeEnqueuer _enqueuer;
    private readonly MarketingPerformanceRunGuard _guard;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecomputeMarketingPerformanceHandler> _logger;

    public RecomputeMarketingPerformanceHandler(
        IMarketingPerformanceRecomputeEnqueuer enqueuer,
        MarketingPerformanceRunGuard guard,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<RecomputeMarketingPerformanceHandler> logger)
    {
        _enqueuer = enqueuer;
        _guard = guard;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<RecomputeMarketingPerformanceResponse> Handle(RecomputeMarketingPerformanceRequest request, CancellationToken cancellationToken)
    {
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var range = MonthRangeParser.Parse(request.From, request.To, current, _options.MaxRecomputeRangeMonths);
        if (!range.IsValid)
        {
            return Task.FromResult(new RecomputeMarketingPerformanceResponse(range.Error!.Value, range.Params));
        }

        if (_guard.IsRunning)
        {
            _logger.LogWarning("Marketing performance recompute {From}..{To} rejected: a run is in progress", range.From, range.To);
            return Task.FromResult(new RecomputeMarketingPerformanceResponse(ErrorCodes.MarketingPerformanceRecomputeAlreadyRunning));
        }

        var jobId = _enqueuer.Enqueue(range.From, range.To);
        if (jobId is null)
        {
            return Task.FromResult(new RecomputeMarketingPerformanceResponse(ErrorCodes.MarketingPerformanceEnqueueFailed));
        }

        _logger.LogInformation("Marketing performance recompute {From}..{To} enqueued as Hangfire job {JobId}", range.From, range.To, jobId);
        return Task.FromResult(new RecomputeMarketingPerformanceResponse { JobId = jobId, MonthCount = range.MonthCount });
    }
}
