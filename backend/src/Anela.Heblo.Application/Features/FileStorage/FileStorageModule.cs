using Anela.Heblo.Application.Features.FileStorage.Services;
using Anela.Heblo.Domain.Features.FileStorage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.FileStorage;

public static class FileStorageModule
{
    public static IServiceCollection AddFileStorageModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR handlers are automatically registered by AddMediatR scan

        // Register Azure Blob Storage client
        var connectionString = configuration["ExpeditionList:BlobConnectionString"];
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddSingleton<BlobServiceClient>(provider => new BlobServiceClient(connectionString));
        }
        else
        {
            // For development, use Azure Storage Emulator
            services.AddSingleton<BlobServiceClient>(provider => new BlobServiceClient("UseDevelopmentStorage=true"));
        }

        // Register HTTP client for file downloads
        services.AddTransient<HttpClient>();

        // Register blob storage service as Singleton so the _containerExists cache survives across requests.
        // BlobServiceClient is already Singleton and HttpClient is safe to reuse — no thread-safety concerns.
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }
}