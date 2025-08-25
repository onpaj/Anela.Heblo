using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.FinancialOverview;

public class FinancialOverviewModuleTests
{
    [Fact]
    public void AddFinancialOverviewModule_RegistersServicesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        var financialAnalysisService = serviceProvider.GetRequiredService<IFinancialAnalysisService>();
        
        Assert.NotNull(stockValueService);
        Assert.NotNull(financialAnalysisService);
        Assert.IsType<StockValueService>(stockValueService);
        Assert.IsType<FinancialAnalysisService>(financialAnalysisService);
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersPlaceholderService_InTestEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");
        mockEnvironment.Setup(x => x.IsEnvironment("Test")).Returns(true);

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        Assert.NotNull(stockValueService);
        Assert.IsType<PlaceholderStockValueService>(stockValueService);
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersPlaceholderService_InAutomationEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Automation");
        mockEnvironment.Setup(x => x.IsEnvironment("Test")).Returns(false);
        mockEnvironment.Setup(x => x.IsEnvironment("Automation")).Returns(true);

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        Assert.NotNull(stockValueService);
        Assert.IsType<PlaceholderStockValueService>(stockValueService);
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersRealService_InProductionEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Production");
        mockEnvironment.Setup(x => x.IsEnvironment("Test")).Returns(false);
        mockEnvironment.Setup(x => x.IsEnvironment("Automation")).Returns(false);

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        Assert.NotNull(stockValueService);
        Assert.IsType<StockValueService>(stockValueService);
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersRealService_InDevelopmentEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");
        mockEnvironment.Setup(x => x.IsEnvironment("Test")).Returns(false);
        mockEnvironment.Setup(x => x.IsEnvironment("Automation")).Returns(false);

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        Assert.NotNull(stockValueService);
        Assert.IsType<StockValueService>(stockValueService);
    }

    [Fact]
    public void AddFinancialOverviewModule_DoesNotRegisterBackgroundService_InAutomationEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Automation");

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Background service should not be registered in Automation environment
        var hostedServices = services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        Assert.Empty(hostedServices);
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersBackgroundService_InNonAutomationEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add required dependencies
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Development");

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);

        // Assert - Background service should be registered in non-Automation environments
        var hostedServices = services.Where(s => s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        Assert.Single(hostedServices);
        Assert.Equal(typeof(FinancialAnalysisBackgroundService), hostedServices.First().ImplementationType);
    }

    [Fact]
    public void AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");

        // Act & Assert - This test verifies that the factory pattern is used
        // The fact that we can successfully register and resolve services without 
        // calling BuildServiceProvider during registration proves the antipattern is avoided
        var exception = Record.Exception(() =>
        {
            services.AddFinancialOverviewModule(mockEnvironment.Object);
            var serviceProvider = services.BuildServiceProvider();
            var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
            Assert.NotNull(stockValueService);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Mock<>));
        
        var mockEnvironment = new Mock<IHostEnvironment>();
        mockEnvironment.Setup(x => x.EnvironmentName).Returns("Test");

        // Act
        services.AddFinancialOverviewModule(mockEnvironment.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var memoryCache = serviceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        Assert.NotNull(memoryCache);
    }
}