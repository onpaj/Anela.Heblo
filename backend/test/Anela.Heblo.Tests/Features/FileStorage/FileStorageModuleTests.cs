using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class FileStorageModuleTests
{
    [Fact]
    public void AddFileStorageModule_RegistersBlobStorageService_AsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var configuration = new ConfigurationBuilder().Build();

        // Act
        services.AddFileStorageModule(configuration);

        // Assert — IBlobStorageService must be Singleton so _containerExists cache survives requests
        var descriptor = services.Single(s => s.ServiceType == typeof(IBlobStorageService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddFileStorageModule_ResolvingBlobStorageServiceTwice_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var configuration = new ConfigurationBuilder().Build();

        services.AddFileStorageModule(configuration);
        var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IBlobStorageService>();
        var second = provider.GetRequiredService<IBlobStorageService>();

        // Assert — same instance proves Singleton registration is working
        Assert.Same(first, second);
    }
}
