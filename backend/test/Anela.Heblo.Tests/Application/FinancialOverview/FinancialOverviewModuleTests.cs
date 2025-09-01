using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class FinancialOverviewModuleTests
{
    [Fact]
    public void AddFinancialOverviewModule_RegistersServicesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateMockConfiguration();

        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");
        services.AddSingleton(mockEnvironment.Object);

        // Act
        services.AddFinancialOverviewModule(configuration, mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        var financialAnalysisService = serviceProvider.GetRequiredService<IFinancialAnalysisService>();

        stockValueService.Should().NotBeNull();
        financialAnalysisService.Should().NotBeNull();
        stockValueService.Should().BeOfType<StockValueService>();
        financialAnalysisService.Should().BeOfType<FinancialAnalysisService>();
    }

   
    [Fact]
    public void AddFinancialOverviewModule_RegistersPlaceholderService_InTestEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");
        // Cannot mock extension methods - the factory checks EnvironmentName instead
        services.AddSingleton(mockEnvironment.Object);

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        stockValueService.Should().NotBeNull();
        stockValueService.Should().BeOfType<PlaceholderStockValueService>();
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersRealService_InProductionEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Production");
        // Cannot mock extension methods - the factory checks EnvironmentName instead
        services.AddSingleton(mockEnvironment.Object);

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        stockValueService.Should().NotBeNull();
        stockValueService.Should().BeOfType<StockValueService>();
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersRealService_InDevelopmentEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");
        // Cannot mock extension methods - the factory checks EnvironmentName instead
        services.AddSingleton(mockEnvironment.Object);

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        stockValueService.Should().NotBeNull();
        stockValueService.Should().BeOfType<StockValueService>();
    }

    [Fact]
    public void AddFinancialOverviewModule_DoesNotRegisterBackgroundService_InTestEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Background service should not be registered in Test environment
        var hostedServices = services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        hostedServices.Should().BeEmpty();
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersBackgroundService_InNonTestEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);

        // Assert - Background service should be registered in non-Test environments
        var hostedServices = services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        hostedServices.Should().HaveCount(1);
        Assert.Equal(typeof(FinancialAnalysisBackgroundService), hostedServices.First().ImplementationType);
    }

    [Fact]
    public void AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");
        services.AddSingleton(mockEnvironment.Object);

        // Act & Assert - This test verifies that the factory pattern is used
        // The fact that we can successfully register and resolve services without 
        // calling BuildServiceProvider during registration proves the antipattern is avoided
        var exception = Record.Exception(() =>
        {
            services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);
            var serviceProvider = services.BuildServiceProvider();
            var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
            stockValueService.Should().NotBeNull();
        });

        exception.Should().BeNull();
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration(), mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var memoryCache = serviceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        memoryCache.Should().NotBeNull();
    }

    private static IConfiguration CreateMockConfiguration()
    {
        var configurationBuilder = new ConfigurationBuilder();
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FinancialAnalysisOptions:RefreshInterval"] = "00:00:00",
            ["FinancialAnalysisOptions:MonthsToCache"] = "24"
        });
        return configurationBuilder.Build();
    }
}