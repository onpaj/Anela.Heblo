using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.FileStorage;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class FileStorageModuleTests
{
    private static IServiceCollection BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Mock.Of<ITelemetryService>());
        services.Configure<ProductExportOptions>(opts =>
        {
            opts.MaxRetryAttempts = 3;
            opts.DownloadTimeout = TimeSpan.FromSeconds(120);
            opts.RetryBaseDelay = TimeSpan.FromSeconds(2);
        });
        return services;
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder().Build();

    [Fact]
    public void AddFileStorageModule_RegistersBlobStorageService_AsSingleton()
    {
        // Arrange
        var services = BuildBaseServices();

        // Act
        services.AddFileStorageModule(BuildConfiguration());

        // Assert — IBlobStorageService must be Singleton so _containerExists cache survives requests
        var descriptor = services.Single(s => s.ServiceType == typeof(IBlobStorageService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddFileStorageModule_ResolvingBlobStorageServiceTwice_ReturnsSameInstance()
    {
        // Arrange
        var services = BuildBaseServices();
        services.AddFileStorageModule(BuildConfiguration());
        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IBlobStorageService>();
        var second = provider.GetRequiredService<IBlobStorageService>();

        // Assert — same instance proves Singleton registration is working
        Assert.Same(first, second);
    }

    [Fact]
    public void AddFileStorageModule_RegistersNamedHttpClient_ProductExportDownload()
    {
        // Arrange
        var services = BuildBaseServices();
        services.AddFileStorageModule(BuildConfiguration());
        var provider = services.BuildServiceProvider();

        // Act
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(FileStorageModule.ProductExportDownloadClientName);

        // Assert — named client is registered and timeout is infinite (per-call CTS enforces timeout)
        Assert.NotNull(client);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void AddFileStorageModule_DoesNotRegisterTransientHttpClient()
    {
        // Arrange
        var services = BuildBaseServices();

        // Act
        services.AddFileStorageModule(BuildConfiguration());

        // Assert — the old services.AddTransient<HttpClient>() self-registers HttpClient with
        // ImplementationType == typeof(HttpClient). AddHttpClient(...) registers a transient with
        // an ImplementationFactory instead, which is the correct IHttpClientFactory pattern.
        // We check for the explicit self-registration to confirm the bug is gone.
        var hasBareTransientHttpClient = services.Any(d =>
            d.ServiceType == typeof(HttpClient) &&
            d.Lifetime == ServiceLifetime.Transient &&
            d.ImplementationType == typeof(HttpClient));

        Assert.False(hasBareTransientHttpClient);
    }

    [Fact]
    public void AddFileStorageModule_RegistersDownloadResilienceService_AsSingleton()
    {
        // Arrange
        var services = BuildBaseServices();

        // Act
        services.AddFileStorageModule(BuildConfiguration());

        // Assert — IDownloadResilienceService must be Singleton with the correct implementation
        var descriptor = services.Single(d => d.ServiceType == typeof(IDownloadResilienceService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(DownloadResilienceService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddFileStorageModule_NamedClient_ConstantIsExported()
    {
        // Assert — the constant must be stable so all consumers reference the same string
        Assert.Equal("ProductExportDownload", FileStorageModule.ProductExportDownloadClientName);
    }
}
