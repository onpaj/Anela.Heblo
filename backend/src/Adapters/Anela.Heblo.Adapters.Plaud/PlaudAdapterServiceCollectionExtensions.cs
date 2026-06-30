using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaudOptions>(configuration.GetSection(PlaudOptions.SectionKey));
        services.AddHostedService<PlaudTokenBootstrapper>();

        // Token refresh HTTP client — registered unconditionally so reactive auth-expiry recovery
        // (PlaudCliClient → PlaudTokenRefresher) works in every environment, not only where Key
        // Vault is configured.
        services.AddHttpClient<PlaudTokenRefreshClient>();
        services.AddSingleton<IPlaudTokenRefreshClient>(
            sp => sp.GetRequiredService<PlaudTokenRefreshClient>());

        // Key Vault is optional. When configured, the refresher persists rotated tokens back to KV
        // so a container restart picks up the fresh value (PlaudTokenBootstrapper re-seeds disk
        // from the KV secret). In local dev (no KeyVault:Uri) the refresher writes disk only.
        var keyVaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
        }

        services.AddSingleton<IPlaudTokenRefresher>(sp => new PlaudTokenRefresher(
            sp.GetRequiredService<IPlaudTokenRefreshClient>(),
            sp.GetRequiredService<ILogger<PlaudTokenRefresher>>(),
            sp.GetService<SecretClient>()));

        services.AddSingleton<IPlaudClient, PlaudCliClient>();

        return services;
    }
}
