using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Repositories;

namespace Anela.Heblo.Application.Features.Logistics.Transport;

public static class TransportModule
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

        return services;
    }
}