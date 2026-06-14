using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginSummary;
using Anela.Heblo.Domain.Features.Analytics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// 🔒 PERFORMANCE FIX: Updated tests for new streaming architecture
/// Tests now use mocked analytics repository instead of direct catalog dependency
/// </summary>
public class GetProductMarginSummaryHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            _marginCalculator,
            _monthlyBreakdownGenerator);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsCorrectResponse()
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            GroupingMode = ProductGroupingMode.Products
        };

        var today = DateTime.Today;
        var fromDate = new DateTime(today.Year, 1, 1);
        var toDate = today;

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Type = AnalyticsProductType.Product,
                MarginAmount = 100m,
                M0Amount = 100m,
                M1Amount = 100m,
                M2Amount = 100m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(today.Year, 3, 15), AmountB2B = 10, AmountB2C = 5 }
                }
            },
            new AnalyticsProduct
            {
                ProductCode = "PROD002",
                ProductName = "Product 2",
                Type = AnalyticsProductType.Product,
                MarginAmount = 50m,
                M0Amount = 50m,
                M1Amount = 50m,
                M2Amount = 50m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(today.Year, 4, 20), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };


        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TimeWindow.Should().Be("current-year");
        result.FromDate.Should().Be(fromDate);
        result.ToDate.Should().Be(toDate);
        result.TotalMargin.Should().Be(3000m); // (15 * 100) + (30 * 50) = 1500 + 1500
        result.TopProducts.Count.Should().Be(2);
        Assert.True(result.MonthlyData.Any());
    }

    [Theory]
    [InlineData("current-year")]
    [InlineData("last-6-months")]
    [InlineData("last-12-months")]
    public async Task Handle_DifferentTimeWindows_ParsesCorrectly(string timeWindow)
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest { TimeWindow = timeWindow, GroupingMode = ProductGroupingMode.Products };
        var today = DateTime.Today;
        var expectedDates = timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            "last-6-months" => (today.AddMonths(-6), today),
            "last-12-months" => (today.AddMonths(-12), today),
            _ => (new DateTime(today.Year, 1, 1), today)
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TimeWindow.Should().Be(timeWindow);
        result.FromDate.Should().Be(expectedDates.Item1);
        result.ToDate.Should().Be(expectedDates.Item2);
    }

    [Fact]
    public async Task Handle_EmptyProductList_ReturnsZeroMargin()
    {
        // Arrange
        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            GroupingMode = ProductGroupingMode.Products
        };

        var today = DateTime.Today;
        var fromDate = new DateTime(today.Year, 1, 1);
        var toDate = today;


        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.TotalMargin.Should().Be(0m);
        result.TopProducts.Should().BeEmpty();
        result.MonthlyData.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator()
    {
        // Arrange
        var marginCalculatorMock = new Mock<IMarginCalculator>();
        var monthlyBreakdownGeneratorMock = new Mock<IMonthlyBreakdownGenerator>();

        var request = new GetProductMarginSummaryRequest
        {
            TimeWindow = "current-year",
            GroupingMode = ProductGroupingMode.Products,
            MarginLevel = MarginLevel.M2
        };

        var today = DateTime.Today;
        var fromDate = new DateTime(today.Year, 1, 1);
        var toDate = today;

        var calculationResult = new MarginCalculationResult
        {
            GroupTotals = new Dictionary<string, decimal> { ["PROD001"] = 500m },
            GroupProducts = new Dictionary<string, List<AnalyticsProduct>>
            {
                ["PROD001"] = new List<AnalyticsProduct>
                {
                    new AnalyticsProduct
                    {
                        ProductCode = "PROD001",
                        ProductName = "Product 1",
                        Type = AnalyticsProductType.Product,
                        MarginAmount = 50m,
                        M0Amount = 50m,
                        M1Amount = 50m,
                        M2Amount = 50m,
                        SellingPrice = 100m,
                        PurchasePrice = 50m,
                        SalesHistory = new List<SalesDataPoint>
                        {
                            new() { Date = new DateTime(today.Year, 3, 1), AmountB2B = 5, AmountB2C = 5 }
                        }
                    }
                }
            },
            TotalMargin = 500m
        };

        var monthlyData = new List<MonthlyProductMarginDto>
        {
            new MonthlyProductMarginDto
            {
                Year = today.Year,
                Month = 3,
                MonthDisplay = "Mar",
                ProductSegments = new List<ProductMarginSegmentDto>(),
                TotalMonthMargin = 500m
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<AnalyticsProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        marginCalculatorMock
            .Setup(x => x.CalculateAsync(
                It.IsAny<IAsyncEnumerable<AnalyticsProduct>>(),
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                MarginLevel.M2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(calculationResult);

        marginCalculatorMock
            .Setup(x => x.GetGroupDisplayName(
                It.IsAny<string>(),
                It.IsAny<ProductGroupingMode>(),
                It.IsAny<List<AnalyticsProduct>>()))
            .Returns<string, ProductGroupingMode, List<AnalyticsProduct>>((key, _, _) => key);

        monthlyBreakdownGeneratorMock
            .Setup(x => x.Generate(
                calculationResult,
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                MarginLevel.M2))
            .Returns(monthlyData);

        var handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object,
            marginCalculatorMock.Object,
            monthlyBreakdownGeneratorMock.Object);

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalMargin.Should().Be(500m);
        result.MonthlyData.Should().BeSameAs(monthlyData);

        marginCalculatorMock.Verify(
            x => x.CalculateAsync(
                It.IsAny<IAsyncEnumerable<AnalyticsProduct>>(),
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                MarginLevel.M2,
                It.IsAny<CancellationToken>()),
            Times.Once);

        monthlyBreakdownGeneratorMock.Verify(
            x => x.Generate(
                calculationResult,
                It.IsAny<DateRange>(),
                ProductGroupingMode.Products,
                MarginLevel.M2),
            Times.Once);
    }

    [Fact]
    public void GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var product = new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Product 1",
            Type = AnalyticsProductType.Product,
            MarginAmount = 50m,
            M0Amount = 10m,
            M1Amount = 20m,
            M2Amount = 30m,
            SalesHistory = new List<SalesDataPoint>()
        };
        var undefined = (MarginLevel)99;

        // Act
        Action act = () => _marginCalculator.GetMarginAmountForLevel(product, undefined);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("marginLevel");
    }
}

// Extension method to convert list to IAsyncEnumerable for testing
public static class TestExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask; // Satisfy async requirement
    }
}