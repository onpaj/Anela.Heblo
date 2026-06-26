using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Persistence.Infrastructure.Resilience;

public sealed class DbResiliencePipelineProvider : IDbResiliencePipelineProvider
{
    private readonly DbResilienceMetrics _metrics;
    private readonly ILogger<DbResiliencePipelineProvider> _logger;

    public DbResiliencePipelineProvider(
        IOptions<DbResilienceOptions> options,
        DbResilienceMetrics metrics,
        ILogger<DbResiliencePipelineProvider> logger)
    {
        _metrics = metrics;
        _logger = logger;
        Pipeline = BuildPipeline(options.Value);
    }

    public ResiliencePipeline Pipeline { get; }

    private ResiliencePipeline BuildPipeline(DbResilienceOptions options)
    {
        var retry = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(TransientErrorClassifier.IsTransient),
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.BaseDelay,
            MaxDelay = options.MaxRetryDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = OnRetry,
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retry)
            .AddTimeout(options.TotalTimeBudget)
            .Build();
    }

    private ValueTask OnRetry(OnRetryArguments<object> args)
    {
        var exception = args.Outcome.Exception;
        var exceptionType = exception?.GetType().FullName ?? "unknown";

        _metrics.RecordRetryAttempt(exceptionType, args.AttemptNumber + 1);

        _logger.LogWarning(
            exception,
            "DbTransientRetry attempt={Attempt} delay={DelayMs}ms exception.type={ExceptionType}",
            args.AttemptNumber + 1,
            args.RetryDelay.TotalMilliseconds,
            exceptionType);

        return ValueTask.CompletedTask;
    }
}
