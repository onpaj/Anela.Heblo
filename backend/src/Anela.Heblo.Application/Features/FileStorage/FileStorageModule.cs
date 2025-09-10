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
        var connectionString = configuration.GetConnectionString("AzureStorage");
        if (!string.IsNullOrEmpty(connectionString))
        {
            services.AddSingleton<BlobServiceClient>(provider => new BlobServiceClient(connectionString));
        }
        else
        {
            // For development, use Azure Storage Emulator
            var emulatorConnectionString = "UseDevelopmentStorage=true";
            services.AddSingleton<BlobServiceClient>(provider => new BlobServiceClient(emulatorConnectionString));
        }

        // Register HTTP client for file downloads
        services.AddTransient<HttpClient>();

        // Register blob storage service
        services.AddTransient<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }
}