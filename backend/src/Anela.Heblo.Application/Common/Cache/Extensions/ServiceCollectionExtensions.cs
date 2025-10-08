using Anela.Heblo.Application.Common.Cache.Abstractions;
using Anela.Heblo.Application.Common.Cache.HealthChecks;
using Anela.Heblo.Application.Common.Cache.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Common.Cache.Extensions;

public static class ServiceCollectionExtensions
{
    public static ICacheRegistrationBuilder AddProactiveCache(this IServiceCollection services)
    {
        // Register TimeProvider if not already registered
        services.AddSingleton(TimeProvider.System);

        // Register the orchestrator as both a singleton and a hosted service
        services.AddSingleton<ProactiveCacheOrchestrator>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ProactiveCacheOrchestrator>());

        // Register health check
        services.AddHealthChecks()
            .AddCheck<ProactiveCacheHealthCheck>("proactive_cache");

        // Create and return the registration builder
        var builder = new CacheRegistrationBuilder(services);

        // Configure the orchestrator when the service provider is built
        services.AddSingleton<IHostedService>(provider =>
        {
            var orchestrator = provider.GetRequiredService<ProactiveCacheOrchestrator>();
            builder.ConfigureOrchestrator(provider, orchestrator);
            return orchestrator;
        });

        return builder;
    }
}