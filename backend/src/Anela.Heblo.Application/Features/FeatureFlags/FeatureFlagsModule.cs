using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Domain.Features.FeatureFlags;
using Anela.Heblo.Persistence.FeatureFlags;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using OpenFeature;

namespace Anela.Heblo.Application.Features.FeatureFlags;

public static class FeatureFlagsModule
{
    public static IServiceCollection AddFeatureFlagsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        // Enables IFeatureManager for [FeatureGate] attribute support on controllers.
        services.AddFeatureManagement(configuration.GetSection("FeatureManagement"));
        services.AddSingleton<HebloFeatureProvider>();
        // Re-created per scope to allow future per-request EvaluationContext injection.
        services.AddScoped<IFeatureClient>(_ => Api.Instance.GetClient());
        services.AddScoped<IFeatureFlagChecker, FeatureFlagChecker>();

        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IFeatureFlagOverrideRepository, FeatureFlagOverrideRepository>();
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
