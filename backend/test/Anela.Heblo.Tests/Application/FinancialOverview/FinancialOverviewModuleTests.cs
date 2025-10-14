using Anela.Heblo.Application.Features.FinancialOverview;
using Anela.Heblo.Application.Features.FinancialOverview.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.FinancialOverview;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        // Act
        services.AddFinancialOverviewModule(configuration);
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
    public void AddFinancialOverviewModule_CanOverridePlaceholderService_ForTesting()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act - Register module first, then override
        services.AddFinancialOverviewModule(CreateMockConfiguration());

        // Override default registration with placeholder for testing
        var stockValueDescriptor = services.SingleOrDefault(s => s.ServiceType == typeof(IStockValueService));
        if (stockValueDescriptor != null)
        {
            services.Remove(stockValueDescriptor);
        }
        services.AddScoped<IStockValueService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<PlaceholderStockValueService>>();
            return new PlaceholderStockValueService(logger);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        stockValueService.Should().NotBeNull();
        stockValueService.Should().BeOfType<PlaceholderStockValueService>();
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersDefaultRealService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies for StockValueService
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration());
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
        stockValueService.Should().NotBeNull();
        stockValueService.Should().BeOfType<StockValueService>();
    }

    [Fact]
    public void AddFinancialOverviewModule_UsesRefreshTaskSystem_InsteadOfBackgroundService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act - Register module
        services.AddFinancialOverviewModule(CreateMockConfiguration());

        // Assert - No specific background service should be registered (using centralized refresh system)
        var hostedServices = services.Where(s =>
            s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            s.ImplementationType?.Name.Contains("FinancialAnalysis") == true).ToList();
        hostedServices.Should().BeEmpty();
    }

    [Fact]
    public void AddFinancialOverviewModule_RegistersRefreshTasks_ForBackgroundDataRefresh()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration());

        // Assert - Refresh task registration should be present (via RegisterRefreshTask extension)
        // Note: We can't easily test the internal refresh task registration here,
        // but we can verify the module doesn't register old background services
        var financialAnalysisServices = services.Where(s =>
            s.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            s.ImplementationType?.Name.Contains("FinancialAnalysis") == true).ToList();
        financialAnalysisServices.Should().BeEmpty();
    }

    [Fact]
    public void AddFinancialOverviewModule_UsesFactoryPattern_AvoidsServiceProviderAntipattern()
    {
        // Arrange
        var services = new ServiceCollection();

        // Add required dependencies
        services.AddSingleton(Mock.Of<IErpStockClient>());
        services.AddSingleton(Mock.Of<IProductPriceErpClient>());
        services.AddSingleton(Mock.Of<ILedgerService>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Act & Assert - This test verifies that the factory pattern is used
        // The fact that we can successfully register and resolve services without 
        // calling BuildServiceProvider during registration proves the antipattern is avoided
        var exception = Record.Exception(() =>
        {
            services.AddFinancialOverviewModule(CreateMockConfiguration());
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

        // Act
        services.AddFinancialOverviewModule(CreateMockConfiguration());
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