using Anela.Heblo.Xcc.Http;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard.Tiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Xcc;

/// <summary>
/// Cross-cutting concerns module registration
/// </summary>
public static class XccModule
{
    public static IServiceCollection AddXccServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register background refresh services with configuration support
        services.AddBackgroundRefresh(configuration);

        // Register dashboard services
        services.Configure<DashboardOptions>(configuration.GetSection(DashboardOptions.SectionName));
        services.AddSingleton<ITileRegistry, TileRegistry>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Register system tiles
        services.RegisterTile<BackgroundTaskStatusTile>();

        // Outbound HTTP observability + connection-pool defaults.
        // Handler is Transient because IHttpClientFactory creates a new handler chain per
        // CreateClient() call (and recycles the primary handler per HandlerLifetime).
        services.Configure<OutboundResilienceOptions>(configuration.GetSection(OutboundResilienceOptions.SectionName));
        services.AddTransient<OutboundCallObservabilityHandler>();
        services.AddHebloOutboundResiliencePipelines();

        return services;
    }
}