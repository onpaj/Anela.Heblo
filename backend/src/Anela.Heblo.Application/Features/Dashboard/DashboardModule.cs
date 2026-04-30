using Anela.Heblo.Application.Features.Dashboard.Tiles;
using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services)
    {
        // MediatR handlers are automatically registered by the ApplicationModule

        // Register dashboard tiles
        services.RegisterTile<PurchaseOrdersInTransitTile>();
        services.RegisterTile<DataQualityStatusTile>();

        return services;
    }
}