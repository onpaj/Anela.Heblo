// backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Shared.Printing;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Azure;

public static class AzureAdapterModule
{
    /// <summary>
    /// Registers Azure Blob print-queue infrastructure (BlobContainerClient, AzureBlobPrintQueueSink
    /// as a concrete singleton) without binding a non-keyed IPrintQueueSink. Use this when the caller
    /// will register its own (e.g. keyed) IPrintQueueSink binding, such as the "Combined" print-sink mode.
    /// </summary>
    public static IServiceCollection AddAzurePrintQueueSinkInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
            return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
        });

        services.AddSingleton<AzureBlobPrintQueueSink>();

        return services;
    }

    /// <summary>
    /// Registers Azure Blob print-queue infrastructure and binds the non-keyed IPrintQueueSink
    /// singleton to AzureBlobPrintQueueSink. Use this for the "AzureBlob" print-sink mode, where
    /// AzureBlobPrintQueueSink is the sole, directly-resolvable IPrintQueueSink implementation.
    /// </summary>
    public static IServiceCollection AddAzurePrintQueueSink(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAzurePrintQueueSinkInfrastructure(configuration);
        services.AddSingleton<IPrintQueueSink>(provider => provider.GetRequiredService<AzureBlobPrintQueueSink>());
        return services;
    }
}
