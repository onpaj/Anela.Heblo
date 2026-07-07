using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Domain.Features.FileStorage;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    public void AddFileStorageModule_RegistersNamedHttpClient_FileDownload()
    {
        // Arrange
        var services = BuildBaseServices();
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));
        var provider = services.BuildServiceProvider();

        // Act
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(FileStorageModule.FileDownloadClientName);

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
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));

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
        services.AddFileStorageModule(BuildConfiguration(), BuildEnvironment(Environments.Development));

        // Assert — IDownloadResilienceService must be Singleton with the correct implementation
        var descriptor = services.Single(d => d.ServiceType == typeof(IDownloadResilienceService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(DownloadResilienceService), descriptor.ImplementationType);
    }

    [Fact]
    public void AddFileStorageModule_NamedClient_ConstantIsExported()
    {
        // Assert — the constant must be stable so all consumers reference the same string
        Assert.Equal("FileDownload", FileStorageModule.FileDownloadClientName);
    }

    [Fact]
    public void AddFileStorageModule_NonDevelopmentEnvironmentWithMissingKey_FailsValidation()
    {
        // Arrange — Production environment with no FileStorage:BlobConnectionString seeded
        var services = BuildBaseServices();
        var configuration = BuildConfiguration(blobConnectionString: null);
        services.AddFileStorageModule(configuration, BuildEnvironment(Environments.Production));
        var provider = services.BuildServiceProvider();

        // Act — resolving IOptions<FileStorageOptions>.Value triggers the same .Validate pipeline
        // that ValidateOnStart() runs at host start. This is the unit-test analogue: we want to
        // confirm the rule fires and the message names the missing key (per spec NFR-2: no value
        // leakage; the key name is mentioned, not the offending value).
        var act = () => provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;

        // Assert
        var ex = Assert.Throws<OptionsValidationException>(act);
        Assert.Contains("FileStorage:BlobConnectionString", ex.Message);
    }
}
