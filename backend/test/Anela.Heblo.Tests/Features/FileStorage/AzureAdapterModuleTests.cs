using Anela.Heblo.Adapters.Azure;
using Anela.Heblo.Adapters.Azure.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Domain.Features.FileStorage;
using Anela.Heblo.Xcc.Telemetry;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

/// <summary>
/// Tests for AzureAdapterModule.AddAzureBlobStorageService — the BlobServiceClient factory and
/// the IBlobStorageService binding relocated from FileStorageModule to the Azure adapter ring.
///
/// Each test calls BOTH AddFileStorageModule (which binds and validates FileStorageOptions) and
/// AddAzureBlobStorageService (which registers BlobServiceClient + IBlobStorageService). The
/// options binding intentionally stays in FileStorageModule, so the adapter registration depends
/// on it being present.
/// </summary>
public class AzureAdapterModuleTests
{
    private static IServiceCollection BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Mock.Of<ITelemetryService>());
        services.Configure<FileDownloadOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });
        return services;
    }

    private static IConfiguration BuildConfiguration(string? blobConnectionString = "UseDevelopmentStorage=true")
    {
        var dict = new Dictionary<string, string?>();
        if (blobConnectionString is not null)
        {
            dict["FileStorage:BlobConnectionString"] = blobConnectionString;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static IHostEnvironment BuildEnvironment(string environmentName) =>
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environmentName);

    [Fact]
    public void AddAzureBlobStorageService_RegistersBlobStorageService_AsSingleton()
    {
        // Arrange
        var services = BuildBaseServices();
        var environment = BuildEnvironment(Environments.Development);

        // Act
        services.AddFileStorageModule(BuildConfiguration(), environment);
        services.AddAzureBlobStorageService(environment);

        // Assert — IBlobStorageService must be Singleton so _containerExists cache survives requests
        var descriptor = services.Single(s => s.ServiceType == typeof(IBlobStorageService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddAzureBlobStorageService_ResolvingBlobStorageServiceTwice_ReturnsSameInstance()
    {
        // Arrange
        var services = BuildBaseServices();
        var environment = BuildEnvironment(Environments.Development);
        services.AddFileStorageModule(BuildConfiguration(), environment);
        services.AddAzureBlobStorageService(environment);
        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IBlobStorageService>();
        var second = provider.GetRequiredService<IBlobStorageService>();

        // Assert — same instance proves Singleton registration is working
        Assert.Same(first, second);
    }

    [Fact]
    public void AddAzureBlobStorageService_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning()
    {
        // Arrange — Development environment, no FileStorage:BlobConnectionString
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ITelemetryService>());
        services.Configure<FileDownloadOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });

        var warningLogger = new Mock<ILogger<AzureBlobStorageService>>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        // Override the AzureBlobStorageService logger so we can verify the warning was emitted.
        services.AddSingleton(warningLogger.Object);

        var environment = BuildEnvironment(Environments.Development);
        var configuration = BuildConfiguration(blobConnectionString: null);
        services.AddFileStorageModule(configuration, environment);
        services.AddAzureBlobStorageService(environment);
        var provider = services.BuildServiceProvider();

        // Act — resolving the BlobServiceClient runs the factory, which emits the warning
        // and returns a client pointed at UseDevelopmentStorage=true.
        var client = provider.GetRequiredService<BlobServiceClient>();

        // Assert — client is constructed (no throw) and the warning was logged once.
        Assert.NotNull(client);
        warningLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("FileStorage:BlobConnectionString")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
