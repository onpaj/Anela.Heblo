using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

public class DownloadResilienceService : IDownloadResilienceService
{
    private static readonly TimeSpan MaxWallClock = TimeSpan.FromMinutes(20);

    private readonly ILogger<DownloadResilienceService> _logger;
    private readonly ITelemetryService _telemetry;
    private readonly ProductExportOptions _options;

    public DownloadResilienceService(
        IOptions<ProductExportOptions> options,
        ITelemetryService telemetry,
        ILogger<DownloadResilienceService> logger)
    {
        _logger = logger;
        _telemetry = telemetry;
        _options = options.Value;

        var worstCase = TimeSpan.FromTicks(_options.DownloadTimeout.Ticks * (_options.MaxRetryAttempts + 1));
        if (worstCase >= MaxWallClock)
        {
            throw new InvalidOperationException(
                $"ProductExportOptions: MaxRetryAttempts ({_options.MaxRetryAttempts}) * DownloadTimeout ({_options.DownloadTimeout}) " +
                $"must be < 20 minutes; got worst-case {worstCase}.");
        }
    }

    public async Task<T> ExecuteWithResilienceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var pipeline = BuildPipeline<T>(operationName, cancellationToken);
        return await pipeline.ExecuteAsync(
            async ct => await operation(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    private ResiliencePipeline<T> BuildPipeline<T>(string operationName, CancellationToken callerCt)
    {
        return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = _options.RetryBaseDelay,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                    {
                        return PredicateResult.True();
                    }

                    // Polly v8 timeout strategy throws TimeoutRejectedException (not OperationCanceledException)
                    if (args.Outcome.Exception is TimeoutRejectedException)
                    {
                        return PredicateResult.True();
                    }

                    // Inner-scope cancellation that is NOT the caller's token means a transient timeout
                    if (args.Outcome.Exception is OperationCanceledException oce
                        && oce.CancellationToken != callerCt)
                    {
                        return PredicateResult.True();
                    }

                    return PredicateResult.False();
                },
                OnRetry = args =>
                {
                    var attemptNumber = args.AttemptNumber + 1;
                    var ex = args.Outcome.Exception;

                    _logger.LogWarning(
                        "Retry {AttemptNumber} for {OperationName} after {Delay} due to {ExceptionType}",
                        attemptNumber,
                        operationName,
                        args.RetryDelay,
                        ex?.GetType().Name);

                    if (ex != null)
                    {
                        _telemetry.TrackException(ex, new Dictionary<string, string>
                        {
                            ["Job"] = operationName,
                            ["AttemptNumber"] = attemptNumber.ToString(),
                            ["IsTerminal"] = "false",
                        });
                    }

                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.DownloadTimeout,
            })
            .Build();
    }
}
