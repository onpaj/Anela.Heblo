using System;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Common.Cache.Abstractions;
using Anela.Heblo.Application.Common.Cache.Extensions;
using Anela.Heblo.Application.Common.Cache.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Common.Cache;

public class CacheRegistrationBuilderTests
{
    [Fact]
    public void Register_WithDataSourceFactory_RegistersCacheService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        
        var mockDataSource = new Mock<ITestDataSource>();
        var config = new CacheRefreshConfiguration
        {
            Name = "test-cache",
            RefreshInterval = TimeSpan.FromMinutes(15)
        };

        // Act
        var builder = services.AddProactiveCache();
        builder.Register<ITestDataSource, TestData>(
            "test-cache",
            sp => mockDataSource.Object,
            (source, ct) => source.GetDataAsync(ct),
            config);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cacheService = serviceProvider.GetService<IProactiveCacheService<TestData>>();
        Assert.NotNull(cacheService);
        Assert.IsType<ProactiveCacheDecorator<ITestDataSource, TestData>>(cacheService);
    }

    [Fact]
    public void Register_WithoutDataSourceFactory_RegistersCacheService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        
        var mockDataSource = new Mock<ITestDataSource>();
        services.AddSingleton(mockDataSource.Object);
        
        var config = new CacheRefreshConfiguration
        {
            Name = "test-cache",
            RefreshInterval = TimeSpan.FromMinutes(15)
        };

        // Act
        var builder = services.AddProactiveCache();
        builder.Register<ITestDataSource, TestData>(
            "test-cache",
            (source, ct) => source.GetDataAsync(ct),
            config);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cacheService = serviceProvider.GetService<IProactiveCacheService<TestData>>();
        Assert.NotNull(cacheService);
        Assert.IsType<ProactiveCacheDecorator<ITestDataSource, TestData>>(cacheService);
    }

    [Fact]
    public void Register_SetsConfigurationName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        
        var mockDataSource = new Mock<ITestDataSource>();
        var config = new CacheRefreshConfiguration
        {
            RefreshInterval = TimeSpan.FromMinutes(15)
        };

        // Act
        var builder = services.AddProactiveCache();
        builder.Register<ITestDataSource, TestData>(
            "my-custom-cache",
            sp => mockDataSource.Object,
            (source, ct) => source.GetDataAsync(ct),
            config);

        // Assert
        Assert.Equal("my-custom-cache", config.Name);
    }

    [Fact]
    public void Register_MultipleServices_RegistersAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        
        var mockDataSource1 = new Mock<ITestDataSource>();
        var mockDataSource2 = new Mock<ITestDataSource>();
        
        var config1 = new CacheRefreshConfiguration { Name = "cache-1" };
        var config2 = new CacheRefreshConfiguration { Name = "cache-2" };

        // Act
        var builder = services.AddProactiveCache();
        builder.Register<ITestDataSource, TestData>(
            "cache-1",
            sp => mockDataSource1.Object,
            (source, ct) => source.GetDataAsync(ct),
            config1);
        
        builder.Register<ITestDataSource, TestData>(
            "cache-2",
            sp => mockDataSource2.Object,
            (source, ct) => source.GetDataAsync(ct),
            config2);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var cacheServices = serviceProvider.GetServices<IProactiveCacheService<TestData>>();
        Assert.Equal(2, cacheServices.Count());
    }

    [Fact]
    public void AddProactiveCache_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddProactiveCache();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(serviceProvider.GetService<TimeProvider>());
        Assert.NotNull(serviceProvider.GetService<ProactiveCacheOrchestrator>());
        
        // Check that health checks were registered
        var healthCheckService = serviceProvider.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        Assert.NotNull(healthCheckService);
    }
}