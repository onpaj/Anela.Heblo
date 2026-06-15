using Anela.Heblo.Adapters.HomeAssistant.Caching;
using Anela.Heblo.Adapters.HomeAssistant.HealthChecks;
using Anela.Heblo.Adapters.HomeAssistant.Resilience;
using Anela.Heblo.Adapters.HomeAssistant.Telemetry;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Anela.Heblo.Adapters.HomeAssistant;

public static class HomeAssistantAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistantAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantSettings>()
            .Bind(configuration.GetSection(HomeAssistantSettings.ConfigurationKey));

        services.AddMemoryCache();

        services.AddSingleton<HomeAssistantSnapshotCoordinator>();
        services.AddSingleton<HomeAssistantSnapshotMetrics>();
        services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        services.AddTransient<HomeAssistantRetryActivityTaggingHandler>();

        var haClientBuilder = services.AddHttpClient<HomeAssistantConditionsReadingProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<HomeAssistantSettings>>().Value;

            if (string.IsNullOrWhiteSpace(settings.BaseUrl)
                || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                // HomeAssistant not configured — HTTP calls will fail per-sensor and
                // surface as ConditionsReadingSource.Unavailable.
                return;
            }

            client.BaseAddress = baseUri;
            // Per-attempt timeout is enforced by Polly AddTimeout below.
            // Setting HttpClient.Timeout would cancel the entire retry chain.
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
        });

        // Outer: resilience (retry + per-attempt timeout)
        haClientBuilder.AddResilienceHandler("ha-conditions", (builder, context) =>
        {
            var settings = context.ServiceProvider.GetRequiredService<IOptions<HomeAssistantSettings>>().Value;

            builder
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = settings.RetryCount,
                    Delay = TimeSpan.FromMilliseconds(settings.RetryBaseDelayMilliseconds),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    MaxDelay = TimeSpan.FromSeconds(settings.RetryMaxDelaySeconds),
                    ShouldHandle = args => ValueTask.FromResult(
                        HomeAssistantTransientErrorPredicate.IsTransient(
                            args.Outcome.Exception,
                            args.Outcome.Result)),
                })
                .AddTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        });

        // Inner: activity tagging runs inside the retry loop so each per-attempt Activity is tagged.
        haClientBuilder.AddHttpMessageHandler<HomeAssistantRetryActivityTaggingHandler>();

        services.AddTransient<IConditionsReadingProvider>(
            sp => sp.GetRequiredService<HomeAssistantConditionsReadingProvider>());

        return services;
    }
}
