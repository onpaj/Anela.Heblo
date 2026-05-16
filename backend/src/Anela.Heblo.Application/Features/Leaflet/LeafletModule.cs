using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using MediatR;
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

        services.AddScoped<ILeafletChunkSummarizer, LeafletChunkSummarizer>();
        services.AddScoped<ILeafletIndexingService, LeafletIndexingService>();

        services.AddScoped<
            IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>,
            LeafletGenerationPersistenceBehavior>();

        // LeafletIngestionJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs()
        // ILeafletDocumentRepository and ILeafletGenerationRepository are registered in PersistenceModule
        // MediatR handlers are auto-registered via AddApplicationServices() assembly scan

        return services;
    }
}
