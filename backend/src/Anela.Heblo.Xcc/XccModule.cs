using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Services.Dashboard.Tiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Xcc;

/// <summary>
/// Cross-cutting concerns module registration
/// </summary>
public static class XccModule
{
    public static IServiceCollection AddXccServices(this IServiceCollection services)
    {
        // Register background refresh services
        services.AddBackgroundRefresh();

        // Register dashboard services
        services.AddSingleton<ITileRegistry, TileRegistry>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Register system tiles
        services.RegisterTile<BackgroundTaskStatusTile>();

        return services;
    }
}