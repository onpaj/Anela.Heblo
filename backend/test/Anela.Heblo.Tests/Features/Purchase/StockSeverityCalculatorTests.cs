using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.Purchase.Model;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class StockSeverityCalculatorTests
{
    private readonly StockSeverityCalculator _calculator;

    public StockSeverityCalculatorTests()
    {
        _calculator = new StockSeverityCalculator();
    }

    [Fact]
    public void DetermineStockSeverity_WhenNotConfigured_ReturnsNotConfigured()
    {
        // Arrange
        var availableStock = 100.0;
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = false;
        var isOptimalConfigured = false;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.NotConfigured, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenBelowMinimum_ReturnsCritical()
    {
        // Arrange
        var availableStock = 40.0; // Below minimum (50)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Critical, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenBelow20PercentOptimal_ReturnsCritical()
    {
        // Arrange
        var availableStock = 30.0; // Less than 20% of optimal (200) = 40
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Critical, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenBetween20And70PercentOptimal_ReturnsLow()
    {
        // Arrange
        var availableStock = 100.0; // Between 20% (40) and 70% (140) of optimal (200)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Low, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenAboveOptimalThreshold_ReturnsOverstocked()
    {
        // Arrange
        var availableStock = 350.0; // More than 150% of optimal (200)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Overstocked, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenExactlyOptimalThreshold_ReturnsOptimal()
    {
        // Arrange
        var availableStock = 300.0; // Exactly 150% of optimal (200) - should be Optimal, not Overstocked (> 150% would be Overstocked)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Optimal, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenBetween70And150PercentOptimal_ReturnsOptimal()
    {
        // Arrange
        var availableStock = 200.0; // Between 70% (140) and 150% (300) of optimal (200)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Optimal, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenOnlyMinConfigured_AndAboveMin_ReturnsOptimal()
    {
        // Arrange
        var availableStock = 150.0; // Above minimum (50)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = false; // Only minimum configured

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Optimal, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenExactly20PercentOptimal_ReturnsLow()
    {
        // Arrange
        var availableStock = 40.0; // Exactly 20% of optimal (200)
        var minStock = 30.0; // Below minimum threshold, so 20% optimal rule applies
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Low, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenExactly70PercentOptimal_ReturnsOptimal()
    {
        // Arrange
        var availableStock = 140.0; // Exactly 70% of optimal (200)
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = true;
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Optimal, result);
    }

    [Fact]
    public void DetermineStockSeverity_WhenOnlyOptimalConfigured_ReturnsOptimalOrOverstocked()
    {
        // Arrange
        var availableStock = 350.0; // Above 150% threshold
        var minStock = 50.0;
        var optimalStock = 200.0;
        var isMinConfigured = false; // Only optimal configured
        var isOptimalConfigured = true;

        // Act
        var result = _calculator.DetermineStockSeverity(availableStock, minStock, optimalStock, isMinConfigured, isOptimalConfigured);

        // Assert
        Assert.Equal(StockSeverity.Overstocked, result);
    }
}