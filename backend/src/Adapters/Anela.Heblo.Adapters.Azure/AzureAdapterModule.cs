// backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs
using Anela.Heblo.Adapters.Azure.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Azure;

public static class AzureAdapterModule
{
    public static IServiceCollection AddAzurePrintQueueSink(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
            return new BlobContainerClient(options.BlobConnectionString, options.BlobContainerName);
        });

        services.AddScoped<IPrintQueueSink, AzureBlobPrintQueueSink>();

        return services;
    }
}
