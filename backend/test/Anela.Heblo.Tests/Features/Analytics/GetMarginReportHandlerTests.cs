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
/// Unit tests for GetMarginReportHandler with result-based error handling
/// </summary>
public class GetMarginReportHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly GetMarginReportHandler _handler;

    public GetMarginReportHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _handler = new GetMarginReportHandler(_analyticsRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsSuccessResponse()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            MaxProducts = 50
        };

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Type = ProductType.Product,
                ProductCategory = "Electronics",
                MarginAmount = 100m,
                SellingPrice = 150m,
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
                ProductCategory = "Books",
                MarginAmount = 50m,
                SellingPrice = 80m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 4, 20), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ReportPeriodStart.Should().Be(request.StartDate);
        result.ReportPeriodEnd.Should().Be(request.EndDate);
        result.TotalProductsAnalyzed.Should().Be(2);
        result.TotalUnitsSold.Should().Be(45); // 15 + 30
        result.ProductSummaries.Should().HaveCount(2);
        result.CategorySummaries.Should().HaveCount(2); // Electronics and Books
        result.TotalMargin.Should().Be(3000m); // (15 * 100) + (30 * 50)
        result.TotalRevenue.Should().Be(4650m); // (15 * 150) + (30 * 80)
    }

    [Fact]
    public async Task Handle_InvalidDateRange_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 12, 31),
            EndDate = new DateTime(2024, 1, 1) // End before start
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
    public async Task Handle_PeriodTooLong_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2020, 1, 1),
            EndDate = new DateTime(2024, 1, 1) // More than 2 years
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidReportPeriod);
        result.Params.Should().ContainKey("period");
    }

    [Fact]
    public async Task Handle_ZeroDaysPeriod_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 1) // Same date
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidReportPeriod);
        result.Params.Should().ContainKey("period");
    }

    [Fact]
    public async Task Handle_NoProductsFound_ReturnsErrorResponse()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            ProductFilter = "NonExistentProduct"
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(new List<AnalyticsProduct>().ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AnalysisDataNotAvailable);
        result.Params.Should().ContainKey("product");
        result.Params.Should().ContainKey("period");
        result.Params["product"].Should().Be("NonExistentProduct");
    }

    [Fact]
    public async Task Handle_ProductFilter_FiltersCorrectly()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            ProductFilter = "Product 1" // Should only return Product 1
        };

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Type = ProductType.Product,
                ProductCategory = "Electronics",
                MarginAmount = 100m,
                SellingPrice = 150m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 }
                }
            },
            new AnalyticsProduct
            {
                ProductCode = "PROD002",
                ProductName = "Different Product",
                Type = ProductType.Product,
                ProductCategory = "Books",
                MarginAmount = 50m,
                SellingPrice = 80m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 4, 20), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.TotalProductsAnalyzed.Should().Be(1);
        result.ProductSummaries.Should().HaveCount(1);
        result.ProductSummaries[0].ProductName.Should().Be("Product 1");
    }

    [Fact]
    public async Task Handle_CategoryFilter_FiltersCorrectly()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            CategoryFilter = "Electronics"
        };

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Type = ProductType.Product,
                ProductCategory = "Electronics",
                MarginAmount = 100m,
                SellingPrice = 150m,
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
                ProductCategory = "Books",
                MarginAmount = 50m,
                SellingPrice = 80m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 4, 20), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.TotalProductsAnalyzed.Should().Be(1);
        result.ProductSummaries[0].Category.Should().Be("Electronics");
        result.CategorySummaries.Should().HaveCount(1);
        result.CategorySummaries[0].Category.Should().Be("Electronics");
    }

    [Fact]
    public async Task Handle_MaxProductsLimit_LimitsResults()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31),
            MaxProducts = 1 // Limit to 1 product
        };

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product 1",
                Type = ProductType.Product,
                ProductCategory = "Electronics",
                MarginAmount = 100m,
                SellingPrice = 150m,
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
                ProductCategory = "Books",
                MarginAmount = 50m,
                SellingPrice = 80m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 4, 20), AmountB2B = 20, AmountB2C = 10 }
                }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.TotalProductsAnalyzed.Should().Be(1); // Limited to 1
        result.ProductSummaries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ProductsWithZeroSales_AreFiltered()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        var analyticsProducts = new List<AnalyticsProduct>
        {
            new AnalyticsProduct
            {
                ProductCode = "PROD001",
                ProductName = "Product with Sales",
                Type = ProductType.Product,
                ProductCategory = "Electronics",
                MarginAmount = 100m,
                SellingPrice = 150m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 3, 15), AmountB2B = 10, AmountB2C = 5 }
                }
            },
            new AnalyticsProduct
            {
                ProductCode = "PROD002",
                ProductName = "Product without Sales",
                Type = ProductType.Product,
                ProductCategory = "Books",
                MarginAmount = 50m,
                SellingPrice = 80m,
                SalesHistory = new List<SalesDataPoint>
                {
                    new() { Date = new DateTime(2024, 4, 20), AmountB2B = 0, AmountB2C = 0 }
                }
            }
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(analyticsProducts.ToAsyncEnumerable());

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.TotalProductsAnalyzed.Should().Be(1); // Only product with sales
        result.ProductSummaries[0].ProductName.Should().Be("Product with Sales");
    }

    [Fact]
    public async Task Handle_RepositoryException_ReturnsInternalServerError()
    {
        // Arrange
        var request = new GetMarginReportRequest
        {
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 12, 31)
        };

        _analyticsRepositoryMock
            .Setup(x => x.StreamProductsWithSalesAsync(request.StartDate, request.EndDate,
                It.IsAny<ProductType[]>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowAsync<AnalyticsProduct>(new Exception("Database connection failed")));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        result.Params.Should().ContainKey("details");
    }

    private static async IAsyncEnumerable<T> ThrowAsync<T>(Exception exception)
    {
        await Task.Yield(); // Make it async
        throw exception;
        yield break; // This will never be reached but is required for the compiler
    }
}