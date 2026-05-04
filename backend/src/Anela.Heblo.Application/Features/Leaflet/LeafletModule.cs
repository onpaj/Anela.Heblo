using Anela.Heblo.Application.Features.Leaflet.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Leaflet;

public static class LeafletModule
{
    public static IServiceCollection AddLeafletModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LeafletOptions>()
            .Bind(configuration.GetSection(LeafletOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ILeafletIndexingService, LeafletIndexingService>();

        // LeafletIngestionJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs()
        // ILeafletRepository is registered in PersistenceModule
        // MediatR handlers are auto-registered via AddApplicationServices() assembly scan

        return services;
    }
}
