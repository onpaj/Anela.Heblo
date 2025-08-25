using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Domain;
using Anela.Heblo.Application.Features.Analytics.Handlers;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Domain.Features.Catalog;
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
    private readonly Mock<IProductMarginAnalysisService> _marginAnalysisServiceMock;
    private readonly MarginCalculator _marginCalculator;
    private readonly MonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly GetProductMarginSummaryHandler _handler;

    public GetProductMarginSummaryHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _marginAnalysisServiceMock = new Mock<IProductMarginAnalysisService>();
        _marginCalculator = new MarginCalculator();
        _monthlyBreakdownGenerator = new MonthlyBreakdownGenerator(_marginCalculator);
        _handler = new GetProductMarginSummaryHandler(
            _analyticsRepositoryMock.Object, 
            _marginAnalysisServiceMock.Object,
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

        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1", 
                Type = ProductType.Product,
                MarginAmount = 100m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 }
                }
            },
            new AnalyticsProduct
            {
                ProductCode = "PROD002",
                ProductName = "Product 2",
                Type = ProductType.Product,
                MarginAmount = 50m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 4, 20), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };

        _marginAnalysisServiceMock
            .Setup(x => x.ParseTimeWindow("current-year"))
            .Returns((fromDate, toDate));

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("current-year", result.TimeWindow);
        Assert.Equal(fromDate, result.FromDate);
        Assert.Equal(toDate, result.ToDate);
        Assert.Equal(3000m, result.TotalMargin); // (15 * 100) + (30 * 50) = 1500 + 1500
        Assert.Equal(2, result.TopProducts.Count);
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
        var expectedDates = (DateTime.Today.AddMonths(-6), DateTime.Today);

        _marginAnalysisServiceMock
            .Setup(x => x.ParseTimeWindow(timeWindow))
            .Returns(expectedDates);

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(timeWindow, result.TimeWindow);
        Assert.Equal(expectedDates.Item1, result.FromDate);
        Assert.Equal(expectedDates.Item2, result.ToDate);
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

        var fromDate = new DateTime(2024, 1, 1);
        var toDate = new DateTime(2024, 12, 31);

        _marginAnalysisServiceMock
            .Setup(x => x.ParseTimeWindow("current-year"))
            .Returns((fromDate, toDate));

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(fromDate, toDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(0m, result.TotalMargin);
        Assert.Empty(result.TopProducts);
        Assert.Empty(result.MonthlyData);
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