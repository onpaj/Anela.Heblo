using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Hangfire storage singleton — resolved lazily after Hangfire is configured
        services.AddSingleton(_ => JobStorage.Current);

        // Register dashboard tiles
        services.RegisterTile<PurchaseOrdersInTransitTile>();
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();
        services.RegisterTile<FailedJobsTile>();

        return services;
    }
}