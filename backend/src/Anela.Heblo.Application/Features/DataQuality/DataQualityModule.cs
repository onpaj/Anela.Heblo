using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Persistence.DataQuality;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.DataQuality;

public static class DataQualityModule
{
    public static IServiceCollection AddDataQualityModule(this IServiceCollection services)
    {
        services.AddScoped<IDqtRunRepository, DqtRunRepository>();
        services.AddScoped<IInvoiceDqtComparer, InvoiceDqtComparer>();
        services.AddScoped<IInvoiceDqtJobRunner, InvoiceDqtJobRunner>();
        services.AddScoped<IDriftDqtJobRunner, DriftDqtJobRunner>();
        services.AddScoped<IDriftDqtComparer, ProductPairingDqtComparer>();
        services.AddScoped<IDriftDqtComparer, StockWriteBackDqtComparer>();

        // Register dashboard tiles
        services.RegisterTile<DataQualityStatusTile>();
        services.RegisterTile<DqtYesterdayStatusTile>();

        return services;
    }
}
