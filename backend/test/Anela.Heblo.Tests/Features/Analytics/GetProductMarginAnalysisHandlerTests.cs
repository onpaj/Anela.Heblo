using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// Unit tests for GetProductMarginAnalysisHandler with result-based error handling
/// </summary>
public class GetProductMarginAnalysisHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly GetProductMarginAnalysisHandler _handler;

    public GetProductMarginAnalysisHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _handler = new GetProductMarginAnalysisHandler(_analyticsRepositoryMock.Object);
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

        _analyticsRepositoryMock
            .Setup(x => x.GetProductAnalysisDataAsync("PROD001", request.StartDate, request.EndDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(productData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ProductId.Should().Be("PROD001");
        result.ProductName.Should().Be("Test Product");
        result.TotalUnitsSold.Should().Be(45); // 10 + 5 + 20 + 10
        result.TotalRevenue.Should().Be(6750m); // 45 * 150
        result.TotalMargin.Should().Be(4500m); // 45 * 100
        result.MarginPercentage.Should().BeApproximately(66.67m, 0.1m); // 4500 / 6750 * 100
        result.MonthlyBreakdown.Should().HaveCount(12); // 12 months in the year
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
    public async Task Handle_ZeroUnitsSold_ReturnsErrorResponse()
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
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InsufficientData);
        result.Params.Should().ContainKey("requiredPeriod");
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