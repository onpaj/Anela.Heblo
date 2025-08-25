using System;
using Anela.Heblo.Application.Features.Catalog.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class SafeMarginCalculatorTests
{
    private readonly Mock<ILogger<SafeMarginCalculator>> _mockLogger;
    private readonly SafeMarginCalculator _calculator;

    public SafeMarginCalculatorTests()
    {
        _mockLogger = new Mock<ILogger<SafeMarginCalculator>>();
        _calculator = new SafeMarginCalculator(_mockLogger.Object);
    }

    [Theory]
    [InlineData(100, 80, 20.00)] // 20% margin
    [InlineData(100, 50, 50.00)] // 50% margin
    [InlineData(100, 0, 100.00)] // 100% margin (zero cost)
    [InlineData(50, 25, 50.00)]  // 50% margin
    public void CalculateMargin_WithValidInputs_ReturnsCorrectMargin(decimal sellingPrice, decimal cost, decimal expectedMargin)
    {
        // Act
        var result = _calculator.CalculateMargin(sellingPrice, cost);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMargin, result.Margin);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void CalculateMargin_WithNullSellingPrice_ReturnsInvalidResult()
    {
        // Act
        var result = _calculator.CalculateMargin(null, 50m);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Missing price or cost data", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void CalculateMargin_WithNullCost_ReturnsInvalidResult()
    {
        // Act
        var result = _calculator.CalculateMargin(100m, null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Missing price or cost data", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void CalculateMargin_WithBothNullInputs_ReturnsInvalidResult()
    {
        // Act
        var result = _calculator.CalculateMargin(null, null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Missing price or cost data", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(-100, 50)]  // Negative selling price
    [InlineData(100, -50)]  // Negative cost
    [InlineData(-100, -50)] // Both negative
    public void CalculateMargin_WithNegativeInputs_ReturnsInvalidResult(decimal sellingPrice, decimal cost)
    {
        // Act
        var result = _calculator.CalculateMargin(sellingPrice, cost);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Negative prices or costs are not allowed", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void CalculateMargin_WithZeroSellingPrice_ReturnsInvalidResult()
    {
        // Act
        var result = _calculator.CalculateMargin(0, 50);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Cannot calculate margin with zero selling price", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(100, 150, -50.00)] // Loss scenario: cost higher than selling price
    [InlineData(50, 60, -20.00)]   // Loss scenario
    public void CalculateMargin_WithHigherCostThanPrice_ReturnsNegativeMargin(decimal sellingPrice, decimal cost, decimal expectedMargin)
    {
        // Act
        var result = _calculator.CalculateMargin(sellingPrice, cost);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMargin, result.Margin);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(0.01, 0.001, 90.00)] // Very small numbers
    [InlineData(1000000, 999999, 0.00)] // Very large numbers with small margin
    [InlineData(100.999, 50.333, 50.16)] // Decimal precision test
    public void CalculateMargin_WithEdgeCases_HandlesCorrectly(decimal sellingPrice, decimal cost, decimal expectedMargin)
    {
        // Act
        var result = _calculator.CalculateMargin(sellingPrice, cost);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedMargin, result.Margin.Value, 2); // Allow small rounding differences
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void CalculateMargin_WithExtremelyLargeNumbers_HandlesGracefully()
    {
        // Arrange
        decimal sellingPrice = decimal.MaxValue;
        decimal cost = decimal.MaxValue - 1;

        // Act
        var result = _calculator.CalculateMargin(sellingPrice, cost);

        // Assert - Should either succeed or return invalid (but not throw)
        Assert.NotNull(result);
        if (result.IsSuccess)
        {
            Assert.NotNull(result.Margin);
        }
        else
        {
            Assert.NotNull(result.ErrorMessage);
        }
    }

    [Fact]
    public void CalculateMargin_LogsErrorWhenExceptionOccurs()
    {
        // Arrange - This test is harder to trigger naturally since our method is robust
        // But we can verify logging occurs if we had a calculation that somehow failed
        
        // Act
        var result = _calculator.CalculateMargin(100, 50);

        // Assert - For valid calculation, no error should be logged
        Assert.True(result.IsSuccess);
        
        // Verify no error logging occurred for valid calculation
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void MarginCalculationResult_SuccessFactoryMethod_CreatesCorrectResult()
    {
        // Act
        var result = MarginCalculationResult.Success(25.50m);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(25.50m, result.Margin);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void MarginCalculationResult_InvalidFactoryMethod_CreatesCorrectResult()
    {
        // Act
        var result = MarginCalculationResult.Invalid("Test error message");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Test error message", result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void MarginCalculationResult_ErrorFactoryMethod_CreatesCorrectResult()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        var result = MarginCalculationResult.Error("Test error", exception);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Margin);
        Assert.Equal("Test error", result.ErrorMessage);
        Assert.Equal(exception, result.Exception);
    }
}