using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetProductMarginAnalysis;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// Unit tests for GetProductMarginAnalysisHandler with refactored services
/// </summary>
public class GetProductMarginAnalysisHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly Mock<IReportBuilderService> _reportBuilderServiceMock;
    private readonly GetProductMarginAnalysisHandler _handler;

    public GetProductMarginAnalysisHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _reportBuilderServiceMock = new Mock<IReportBuilderService>();

        _handler = new GetProductMarginAnalysisHandler(
            _analyticsRepositoryMock.Object,
            _reportBuilderServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessResponse()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            IncludeBreakdown = true
        };

        var productData = new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Test Product",
            Type = ProductType.Product,
            MarginAmount = 100m,
            SellingPrice = 150m,
            SalesHistory = new List<SalesDataPoint>
            {
                new() { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 },
                new() { Date = new DateTime(2024, 6, 20), AmountB2B = 20, AmountB2C = 10 }
            }
        };

        var marginData = new AnalysisMarginData
        {
            Revenue = 6750m,
            Cost = 2250m,
            Margin = 4500m,
            MarginPercentage = 66.67m,
            UnitsSold = 45
        };

        var monthlyBreakdown = new List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown>();
        for (int month = 1; month <= 12; month++)
        {
            monthlyBreakdown.Add(new GetProductMarginAnalysisResponse.MonthlyMarginBreakdown
            {
                Month = new DateTime(2024, month, 1),
                UnitsSold = month == 3 ? 15 : month == 6 ? 30 : 0,
                Revenue = month == 3 ? 2250m : month == 6 ? 4500m : 0,
                MarginAmount = month == 3 ? 1500m : month == 6 ? 3000m : 0,
                Cost = month == 3 ? 750m : month == 6 ? 1500m : 0
            });
        }

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("PROD001", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(productData);

        _reportBuilderServiceMock
            .Setup(x => x.BuildMonthlyBreakdown(It.IsAny<List<SalesDataPoint>>(), productData, request.StartDate, request.EndDate))
            .Returns(monthlyBreakdown);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ProductId.Should().Be("PROD001");
        result.ProductName.Should().Be("Test Product");
        result.TotalUnitsSold.Should().Be(45);
        result.TotalRevenue.Should().Be(6750m);
        result.TotalMargin.Should().Be(4500m);
        result.MarginPercentage.Should().BeApproximately(66.67m, 0.1m);
        result.MonthlyBreakdown.Should().HaveCount(12);
    }

    [Fact]
    public async Task Handle_InvalidDateRange_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 12, 31),
            EndDate = new DateTime(2024, 1, 1), // End before start
            IncludeBreakdown = true
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidDateRange);
        result.Params.Should().ContainKey("startDate");
        result.Params.Should().ContainKey("endDate");
    }

    [Fact]
    public async Task Handle_EmptyProductId_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "", // Empty product ID
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().ContainKey("field");
        result.Params["field"].Should().Be("ProductId");
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "NONEXISTENT",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("NONEXISTENT", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalyticsProduct?)null);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ProductNotFoundForAnalysis);
        result.Params.Should().ContainKey("productId");
        result.Params["productId"].Should().Be("NONEXISTENT");
    }

    [Fact]
    public async Task Handle_NoSalesDataInPeriod_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        var productData = new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Test Product",
            Type = ProductType.Product,
            MarginAmount = 100m,
            SellingPrice = 150m,
            SalesHistory = new List<SalesDataPoint>
            {
                // Sales outside the requested period
                new() { Date = new DateTime(2023, 3, 15), AmountB2B = 10, AmountB2C = 5 }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("PROD001", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(productData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AnalysisDataNotAvailable);
        result.Params.Should().ContainKey("product");
        result.Params.Should().ContainKey("period");
    }

    [Fact]
    public async Task Handle_ZeroUnitsSold_ReturnsSuccessWithZeroValues()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        var productData = new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Test Product",
            Type = ProductType.Product,
            MarginAmount = 100m,
            SellingPrice = 150m,
            SalesHistory = new List<SalesDataPoint>
            {
                // Sales in period but zero units
                new() { Date = new DateTime(2024, 3, 15), AmountB2B = 0, AmountB2C = 0 }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("PROD001", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(productData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.TotalUnitsSold.Should().Be(0);
        result.TotalRevenue.Should().Be(0m);
        result.TotalCost.Should().Be(0m);
        result.TotalMargin.Should().Be(0m);
        result.MarginPercentage.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_WithoutBreakdown_ReturnsResponseWithoutMonthlyData()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            IncludeBreakdown = false // No breakdown requested
        };

        var productData = new AnalyticsProduct
        {
            ProductCode = "PROD001",
            ProductName = "Test Product",
            Type = ProductType.Product,
            MarginAmount = 100m,
            SellingPrice = 150m,
            SalesHistory = new List<SalesDataPoint>
            {
                new() { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 }
            }
        };

        var marginData = new AnalysisMarginData
        {
            Revenue = 2250m,
            Cost = 750m,
            Margin = 1500m,
            MarginPercentage = 66.67m,
            UnitsSold = 15
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("PROD001", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(productData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.MonthlyBreakdown.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RepositoryException_ReturnsInternalServerError()
    {
        // Arrange
        var request = new GetProductMarginAnalysisRequest
        {
            ProductId = "PROD001",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("PROD001", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.Params.Should().ContainKey("details");
    }
}