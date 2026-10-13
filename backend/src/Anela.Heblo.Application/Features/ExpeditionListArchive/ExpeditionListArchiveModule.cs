using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExpeditionListArchiveOptions>(configuration.GetSection(ExpeditionListArchiveOptions.ConfigurationKey));

        // ReprintExpeditionListHandler is registered by MediatR's assembly scan (see
        // ApplicationModule.AddApplicationServices -> AddMediatR), like every other handler.
        // Its preference for the keyed "cups" IPrintQueueSink, with fallback to the ambient
        // sink, is expressed directly on its own constructor via [FromKeyedServices("cups")]
        // -- no manual IRequestHandler registration here, and no dependency on this module
        // being registered after AddMediatR. See issue #4330.

        return services;
    }
}
