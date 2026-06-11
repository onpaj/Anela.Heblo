using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.FileSystem;

public static class FileSystemAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the filesystem-based <see cref="IPrintQueueSink"/> implementation.
    /// PrintPickingListOptions is bound by ExpeditionListModule in the Application layer,
    /// so this extension takes no IConfiguration parameter.
    /// </summary>
    public static IServiceCollection AddFileSystemPrintQueueSink(this IServiceCollection services)
    {
        services.AddScoped<IPrintQueueSink, FileSystemPrintQueueSink>();
        return services;
    }
}
