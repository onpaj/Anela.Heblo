using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Anela.Heblo.Xcc.Http;

/// <summary>
/// Registers per-dependency Polly v8 ResiliencePipeline instances based on
/// OutboundResilienceOptions.Dependencies. Each dependency entry where
/// RetryEnabled = true gets a pipeline keyed by the dependency name; call sites
/// resolve them via ResiliencePipelineProvider&lt;string&gt;.
///
/// Retries fire on HttpRequestException, TimeoutRejectedException, and on
/// OperationCanceledException only when the caller token has not requested cancellation.
/// </summary>
public static class HebloResiliencePipelineExtensions
{
    public static IServiceCollection AddHebloOutboundResiliencePipelines(this IServiceCollection services)
    {
        // Use a temporary ServiceProvider to read configuration at registration time.
        using var tempSp = services.BuildServiceProvider();
        var options = tempSp.GetRequiredService<IOptionsMonitor<OutboundResilienceOptions>>().CurrentValue;

        foreach (var (name, depOptions) in options.Dependencies)
        {
            if (!depOptions.RetryEnabled)
                continue;

            var localName = name;
            var localOptions = depOptions;

            services.AddResiliencePipeline(localName, (builder, context) =>
            {
                var logger = context.ServiceProvider
                    .GetService<ILoggerFactory>()
                    ?.CreateLogger("Anela.Heblo.Xcc.Http.OutboundResilience");

                BuildPipeline(builder, localName, localOptions, logger);
            });
        }

        // Always register the registry infrastructure so ResiliencePipelineProvider<string>
        // can be resolved even when no dependencies have RetryEnabled = true.
        if (!options.Dependencies.Any(d => d.Value.RetryEnabled))
        {
            services.AddResiliencePipelineRegistry<string>();
        }

        return services;
    }

    private static void BuildPipeline(
        ResiliencePipelineBuilder builder,
        string dependencyName,
        DependencyResilienceOptions options,
        ILogger? logger)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Exception is HttpRequestException)
                        return PredicateResult.True();
                    if (args.Outcome.Exception is TimeoutRejectedException)
                        return PredicateResult.True();
                    if (args.Outcome.Exception is OperationCanceledException
                        && !args.Context.CancellationToken.IsCancellationRequested)
                        return PredicateResult.True();
                    return PredicateResult.False();
                },
                OnRetry = args =>
                {
                    logger?.LogWarning(
                        "Retry {AttemptNumber} for {Dependency} after {Delay} due to {ExceptionType}",
                        args.AttemptNumber + 1,
                        dependencyName,
                        args.RetryDelay,
                        args.Outcome.Exception?.GetType().Name);
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.Timeout,
            });
    }
}
