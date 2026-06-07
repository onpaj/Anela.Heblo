using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence.Features.Leaflet;
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

        // Repositories (implementations live in the Persistence layer)
        services.AddScoped<ILeafletDocumentRepository, LeafletDocumentRepository>();
        services.AddScoped<ILeafletGenerationRepository, LeafletGenerationRepository>();

        // LeafletIngestionJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs()
        // MediatR handlers are auto-registered via AddApplicationServices() assembly scan

        return services;
    }
}
