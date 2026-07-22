using Anela.Heblo.Application.Features.Purchase.Services;
using Xunit;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Purchase;

public class StockAnalysisCalculatorTests
{
    private readonly StockAnalysisCalculator _calculator;

    public StockAnalysisCalculatorTests()
    {
        _calculator = new StockAnalysisCalculator();
    }

    [Fact]
    public void CalculateStockEfficiency_WhenOptimalStockPositive_ReturnsAvailableOverOptimal()
    {
        // Arrange
        var availableStock = 100.0;
        var minStock = 50.0;
        var optimalStock = 200.0;

        // Act
        var result = _calculator.CalculateStockEfficiency(availableStock, minStock, optimalStock);

        // Assert
        result.Should().Be(50.0);
    }

    [Fact]
    public void CalculateStockEfficiency_WhenOptimalStockNotPositiveAndMinStockPositive_ReturnsAvailableOverMin()
    {
        // Arrange
        var availableStock = 25.0;
        var minStock = 50.0;
        var optimalStock = 0.0;

        // Act
        var result = _calculator.CalculateStockEfficiency(availableStock, minStock, optimalStock);

        // Assert
        result.Should().Be(50.0);
    }

    [Fact]
    public void CalculateStockEfficiency_WhenOptimalAndMinStockNotPositive_ReturnsZero()
    {
        // Arrange
        var availableStock = 25.0;
        var minStock = 0.0;
        var optimalStock = 0.0;

        // Act
        var result = _calculator.CalculateStockEfficiency(availableStock, minStock, optimalStock);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenOptimalAndMinStockNotPositive_ReturnsNull()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 0.0;
        var minStock = 0.0;
        var moq = string.Empty;

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenAvailableStockAtOrAboveOptimal_ReturnsNull()
    {
        // Arrange
        var availableStock = 200.0;
        var optimalStock = 150.0;
        var minStock = 50.0;
        var moq = string.Empty;

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenOptimalStockNotPositive_UsesDoubleMinStockAsTarget()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 0.0;
        var minStock = 30.0; // target = 60
        var moq = string.Empty;

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().Be(50.0); // 60 - 10
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenMoqPresentAndGreaterThanShortfall_ReturnsMoq()
    {
        // Arrange
        var availableStock = 90.0;
        var optimalStock = 100.0; // needed = 10
        var minStock = 50.0;
        var moq = "40";

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().Be(40.0);
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenMoqPresentAndLessThanShortfall_ReturnsShortfall()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 100.0; // needed = 90
        var minStock = 50.0;
        var moq = "20";

        // Act
        var result = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, moq);

        // Assert
        result.Should().Be(90.0);
    }

    [Fact]
    public void CalculateRecommendedOrderQuantity_WhenMoqNullOrEmptyOrUnparseable_ReturnsRawShortfall()
    {
        // Arrange
        var availableStock = 10.0;
        var optimalStock = 100.0; // needed = 90
        var minStock = 50.0;

        // Act
        var resultWithNull = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, null!);
        var resultWithEmpty = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, string.Empty);
        var resultWithUnparseable = _calculator.CalculateRecommendedOrderQuantity(availableStock, optimalStock, minStock, "not-a-number");

        // Assert
        resultWithNull.Should().Be(90.0);
        resultWithEmpty.Should().Be(90.0);
        resultWithUnparseable.Should().Be(90.0);
    }
}
