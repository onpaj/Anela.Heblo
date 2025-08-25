using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Configuration;

public class ManufactureAnalysisOptionsTests
{
    [Fact]
    public void ManufactureAnalysisOptions_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var options = new ManufactureAnalysisOptions();

        // Assert
        Assert.Equal(12, options.DefaultMonthsBack);
        Assert.Equal(60, options.MaxMonthsBack);
        Assert.Equal(30, options.ProductionActivityDays);
        Assert.Equal(1.0m, options.CriticalStockMultiplier);
        Assert.Equal(1.5m, options.HighStockMultiplier);
        Assert.Equal(2.0m, options.MediumStockMultiplier);
        Assert.Equal(999999, options.InfiniteStockIndicator);
    }

    [Fact]
    public void ManufactureAnalysisOptions_ShouldBindFromConfiguration()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["ManufactureAnalysis:DefaultMonthsBack"] = "6",
            ["ManufactureAnalysis:MaxMonthsBack"] = "36",
            ["ManufactureAnalysis:ProductionActivityDays"] = "14",
            ["ManufactureAnalysis:CriticalStockMultiplier"] = "0.8",
            ["ManufactureAnalysis:HighStockMultiplier"] = "1.2",
            ["ManufactureAnalysis:MediumStockMultiplier"] = "1.8",
            ["ManufactureAnalysis:InfiniteStockIndicator"] = "888888"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var services = new ServiceCollection();
        services.Configure<ManufactureAnalysisOptions>(options =>
        {
            configuration.GetSection("ManufactureAnalysis").Bind(options);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<ManufactureAnalysisOptions>>().Value;

        // Assert
        Assert.Equal(6, options.DefaultMonthsBack);
        Assert.Equal(36, options.MaxMonthsBack);
        Assert.Equal(14, options.ProductionActivityDays);
        Assert.Equal(0.8m, options.CriticalStockMultiplier);
        Assert.Equal(1.2m, options.HighStockMultiplier);
        Assert.Equal(1.8m, options.MediumStockMultiplier);
        Assert.Equal(888888, options.InfiniteStockIndicator);
    }

    [Fact]
    public void ManufactureAnalysisConstants_ShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal(999, ManufactureAnalysisConstants.UnlimitedAnalysisPeriod);
        Assert.Equal(0.001m, ManufactureAnalysisConstants.MinimumConsumptionRate);
        Assert.Equal(36500, ManufactureAnalysisConstants.MaxReasonableDaysOfStock);
    }
}