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
        services.AddSingleton<ITileRegistry, TileRegistry>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Register system tiles
        services.RegisterTile<BackgroundTaskStatusTile>();

        return services;
    }
}