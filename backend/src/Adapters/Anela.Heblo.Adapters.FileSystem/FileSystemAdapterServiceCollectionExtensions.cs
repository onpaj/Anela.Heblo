using Anela.Heblo.Adapters.FileSystem.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.DependencyInjection;
using ExpeditionListArchiveContracts = Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;

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

    /// <summary>
    /// Registers the filesystem-based <see cref="ITemporaryFileAccessor"/> implementation.
    /// Used by ExpeditionListService to read/delete exported PDFs regardless of which
    /// print sink (ExpeditionList:PrintSink) is configured, since exported files always
    /// land on local disk first. Also registered under the ExpeditionListArchive-owned
    /// contract so ReprintExpeditionListHandler doesn't cross the ExpeditionListArchive
    /// -> ExpeditionList module boundary.
    /// </summary>
    public static IServiceCollection AddFileSystemTemporaryFileAccessor(this IServiceCollection services)
    {
        services.AddScoped<ITemporaryFileAccessor, FileSystemTemporaryFileAccessor>();
        services.AddScoped<ExpeditionListArchiveContracts.ITemporaryFileAccessor, FileSystemTemporaryFileAccessor>();
        return services;
    }
}
