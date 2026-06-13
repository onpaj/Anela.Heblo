using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Anela.Heblo.Xcc.Telemetry;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaudOptions>(configuration.GetSection(PlaudOptions.SectionKey));
        services.Configure<PlaudCredentialsOptions>(configuration.GetSection(PlaudCredentialsOptions.SectionKey));
        services.AddHostedService<PlaudTokenBootstrapper>();

        var keyVaultUri = configuration["KeyVault:Uri"];
        if (string.IsNullOrWhiteSpace(keyVaultUri))
        {
            // Local dev: register the CLI client without refresh capability.
            services.AddSingleton<IPlaudClient, PlaudCliClient>();
            return services;
        }

        services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
        services.AddHttpClient<PlaudTokenRefreshClient>();
        services.AddSingleton<IPlaudTokenRefreshClient>(
            sp => sp.GetRequiredService<PlaudTokenRefreshClient>());
        services.AddSingleton<IPlaudTokenStore, PlaudTokenStore>();
        services.AddSingleton<IPlaudTokenManager, PlaudTokenManager>();

        services.AddSingleton<IPlaudClient>(sp => new PlaudCliClient(
            sp.GetRequiredService<ILogger<PlaudCliClient>>(),
            sp.GetRequiredService<IOptions<PlaudOptions>>(),
            sp.GetRequiredService<IPlaudTokenManager>(),
            sp.GetRequiredService<ITelemetryService>()));

        services.AddScoped<IRecurringJob, PlaudTokenRefreshJob>();

        return services;
    }
}
