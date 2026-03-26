using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionListArchive;

public static class ExpeditionListArchiveModule
{
    public static IServiceCollection AddExpeditionListArchiveModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Uses IBlobStorageService and keyed IPrintQueueSink("cups") registered elsewhere
        return services;
    }
}
