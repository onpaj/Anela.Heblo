using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenFeature;

namespace Anela.Heblo.Application.Features.FeatureFlags;

public static class FeatureFlagsModule
{
    public static IServiceCollection AddFeatureFlagsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<HebloFeatureProvider>();
        services.AddScoped<IFeatureClient>(_ => Api.Instance.GetClient());
        services.AddScoped<IFeatureFlagChecker, FeatureFlagChecker>();
        return services;
    }

    /// <summary>
    /// Must be called after app.Build() to initialize the OpenFeature global provider.
    /// </summary>
    public static async Task InitializeFeatureFlagsAsync(this IHost app)
    {
        var provider = app.Services.GetRequiredService<HebloFeatureProvider>();
        await Api.Instance.SetProviderAsync(provider);
    }
}
