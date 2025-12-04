using Anela.Heblo.Application.Features.Logistics.DashboardTiles;
using Anela.Heblo.Application.Features.Logistics.UseCases.ProcessReceivedBoxes;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Logistics.TransportBoxes;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Anela.Heblo.Xcc.Services.Dashboard;
using MediatR;
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

        // Register dashboard tiles
        services.RegisterTile<InTransitBoxesTile>();
        services.RegisterTile<ReceivedBoxesTile>();
        services.RegisterTile<ErrorBoxesTile>();
        services.RegisterTile<CriticalGiftPackagesTile>();

        // Register background task for automatic processing of received transport boxes
        services.RegisterRefreshTask<ITransportBoxRepository>(
            "ProcessReceivedBoxes",
            async (serviceProvider, cancellationToken) => 
            {
                var mediator = serviceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new ProcessReceivedBoxesRequest(), cancellationToken);
            }
        );

        return services;
    }
}