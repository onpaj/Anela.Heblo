using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Plaud;

public static class PlaudAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddPlaudAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaudOptions>(configuration.GetSection(PlaudOptions.SectionKey));
        services.AddSingleton<IPlaudClient, PlaudCliClient>();
        services.AddHostedService<PlaudTokenBootstrapper>();

        // PlaudTokenRefreshClient is stateless (HTTP only) — always register so PlaudCliClient
        // can auto-refresh on auth expiry regardless of environment.
        services.AddHttpClient<PlaudTokenRefreshClient>();
        services.AddTransient<IPlaudTokenRefreshClient>(
            sp => sp.GetRequiredService<PlaudTokenRefreshClient>());

        // Token refresh job requires Key Vault write access.
        // Skip registration in local dev where KeyVault:Uri is unset.
        var keyVaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
            services.AddScoped<IRecurringJob, PlaudTokenRefreshJob>();
        }

        return services;
    }
}
