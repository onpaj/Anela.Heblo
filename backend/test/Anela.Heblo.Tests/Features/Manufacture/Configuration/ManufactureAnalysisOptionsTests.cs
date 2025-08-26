using Anela.Heblo.Application.Features.Manufacture.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture.Configuration;

public class ManufactureAnalysisOptionsTests
{
    [Fact]
    public void ManufactureAnalysisOptions_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var options = new ManufactureAnalysisOptions();

        // Assert
        options.DefaultMonthsBack.Should().Be(12);
        options.MaxMonthsBack.Should().Be(60);
        options.ProductionActivityDays.Should().Be(30);
        options.CriticalStockMultiplier.Should().Be(1.0m);
        options.HighStockMultiplier.Should().Be(1.5m);
        options.MediumStockMultiplier.Should().Be(2.0m);
        options.InfiniteStockIndicator.Should().Be(999999);
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
        options.DefaultMonthsBack.Should().Be(6);
        options.MaxMonthsBack.Should().Be(36);
        options.ProductionActivityDays.Should().Be(14);
        options.CriticalStockMultiplier.Should().Be(0.8m);
        options.HighStockMultiplier.Should().Be(1.2m);
        options.MediumStockMultiplier.Should().Be(1.8m);
        options.InfiniteStockIndicator.Should().Be(888888);
    }

    [Fact]
    public void ManufactureAnalysisConstants_ShouldHaveCorrectValues()
    {
        // Assert
        ManufactureAnalysisConstants.UnlimitedAnalysisPeriod.Should().Be(999);
        ManufactureAnalysisConstants.MinimumConsumptionRate.Should().Be(0.001m);
        ManufactureAnalysisConstants.MaxReasonableDaysOfStock.Should().Be(36500);
    }
}