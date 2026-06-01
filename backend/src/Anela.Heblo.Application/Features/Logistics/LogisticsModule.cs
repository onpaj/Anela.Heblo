using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Application.Features.Logistics.Infrastructure;
using Anela.Heblo.Application.Features.Logistics.Services;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics;

public static class LogisticsModule
{
    public static IServiceCollection AddTransportModule(this IServiceCollection services)
    {
        // Register repositories using factory pattern to avoid ServiceProvider antipattern
        services.AddScoped<ITransportBoxRepository>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            var logger = provider.GetRequiredService<ILogger<TransportBoxRepository>>();
            return new TransportBoxRepository(context, logger);
        });

        // Register transport box completion service
        services.AddTransient<ITransportBoxCompletionService, TransportBoxCompletionService>();

        // Register dashboard tiles
        services.RegisterTile<InTransitBoxesTile>();
        services.RegisterTile<ReceivedBoxesTile>();
        services.RegisterTile<ErrorBoxesTile>();
        services.RegisterTile<CriticalGiftPackagesTile>();

        // Register background refresh task for completing received boxes
        services.RegisterRefreshTask<ITransportBoxCompletionService>(
            nameof(ITransportBoxCompletionService.CompleteReceivedBoxesAsync),
            (service, ct) => service.CompleteReceivedBoxesAsync(ct)
        );

        // Cross-module contract: Catalog owns ICatalogTransportSource; Logistics implements it via adapter.
        services.AddScoped<ICatalogTransportSource, LogisticsCatalogTransportSourceAdapter>();

        return services;
    }
}