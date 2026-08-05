using Anela.Heblo.Domain.Features.Attendance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Anela.Heblo.Adapters.Logeto;

public static class LogetoAdapterModule
{
    public static IServiceCollection AddLogetoAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LogetoOptions>()
            .Bind(configuration.GetSection(LogetoOptions.ConfigKey));

        var clientBuilder = services.AddHttpClient<LogetoClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<LogetoOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.AccountName))
            {
                // Logeto not configured — the nightly job is disabled by default and any
                // accidental call fails fast with an invalid request URI.
                return;
            }

            client.BaseAddress = new Uri($"https://{options.AccountName}.logeto.com");
            // Per-attempt timeout is enforced by the resilience handler below.
            // Setting HttpClient.Timeout would cancel the entire retry chain.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        clientBuilder.AddResilienceHandler("logeto", (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<LogetoOptions>>().Value;

            builder
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.RetryCount,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                })
                .AddTimeout(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        });

        services.AddTransient<ILogetoClient>(sp => sp.GetRequiredService<LogetoClient>());

        return services;
    }
}
