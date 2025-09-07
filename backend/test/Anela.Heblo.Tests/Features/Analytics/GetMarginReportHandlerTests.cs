using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Infrastructure;
using Anela.Heblo.Application.Features.Analytics.Services;
using Anela.Heblo.Application.Features.Analytics.Models;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

/// <summary>
/// Unit tests for GetMarginReportHandler with refactored services
/// </summary>
public class GetMarginReportHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _analyticsRepositoryMock;
    private readonly Mock<IProductFilterService> _productFilterServiceMock;
    private readonly Mock<IMarginCalculationService> _marginCalculationServiceMock;
    private readonly Mock<IReportBuilderService> _reportBuilderServiceMock;
    private readonly GetMarginReportHandler _handler;

    public GetMarginReportHandlerTests()
    {
        _analyticsRepositoryMock = new Mock<IAnalyticsRepository>();
        _productFilterServiceMock = new Mock<IProductFilterService>();
        _marginCalculationServiceMock = new Mock<IMarginCalculationService>();
        _reportBuilderServiceMock = new Mock<IReportBuilderService>();
        
        _handler = new GetMarginReportHandler(
            _analyticsRepositoryMock.Object,
            _productFilterServiceMock.Object,
            _marginCalculationServiceMock.Object,
            _reportBuilderServiceMock.Object);
        
        // Set up default service behaviors for all tests
        SetupDefaultServiceMocks();
    }

    private void SetupDefaultServiceMocks()
    {
        // Default margin calculation service behavior
        _marginCalculationServiceMock
            .Setup(x => x.CalculateProductMargins(It.IsAny<AnalyticsProduct>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns((AnalyticsProduct product, DateTime start, DateTime end) =>
            {
                var salesInPeriod = product.SalesHistory.Where(s => s.Date >= start && s.Date <= end).ToList();
                var unitsSold = (int)salesInPeriod.Sum(s => s.AmountB2B + s.AmountB2C);
                
                if (unitsSold == 0)
                {
                    return ServiceMarginCalculationResult.Failure(ErrorCodes.InsufficientData);
                }
                
                var revenue = unitsSold * product.SellingPrice;
                var margin = unitsSold * product.MarginAmount;
                var cost = revenue - margin;
                
                return ServiceMarginCalculationResult.Success(new MarginData
                {
                    Revenue = revenue,
                    Cost = cost,
                    Margin = margin,
                    MarginPercentage = revenue > 0 ? (margin / revenue) * 100 : 0,
                    UnitsSold = unitsSold
                });
            });

        // Default product filter service behavior - implements actual filtering logic
        _productFilterServiceMock
            .Setup(x => x.FilterProductsAsync(It.IsAny<IAsyncEnumerable<AnalyticsProduct>>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (IAsyncEnumerable<AnalyticsProduct> products, string productFilter, string categoryFilter, int maxProducts, CancellationToken ct) =>
            {
                var productList = new List<AnalyticsProduct>();
                await foreach (var product in products.WithCancellation(ct))
                {
                    productList.Add(product);
                }

                // Apply product filter
                if (!string.IsNullOrWhiteSpace(productFilter))
                {
                    productList = productList.Where(p => p.ProductName?.Contains(productFilter, StringComparison.OrdinalIgnoreCase) == true).ToList();
                }

                // Apply category filter
                if (!string.IsNullOrWhiteSpace(categoryFilter))
                {
                    productList = productList.Where(p => string.Equals(p.ProductCategory, categoryFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                // Apply max products limit
                if (maxProducts > 0)
                {
                    productList = productList.Take(maxProducts).ToList();
                }

                return productList;
            });

        // Default report builder service behavior
        _reportBuilderServiceMock
            .Setup(x => x.BuildProductSummary(It.IsAny<AnalyticsProduct>(), It.IsAny<MarginData>()))
            .Returns((AnalyticsProduct product, MarginData data) => new GetMarginReportResponse.ProductMarginSummary
            {
                ProductId = product.ProductCode,
                ProductName = product.ProductName,
                Category = product.ProductCategory ?? "Uncategorized",
                MarginAmount = data.Margin,
                MarginPercentage = data.MarginPercentage,
                Revenue = data.Revenue,
                Cost = data.Cost,
                UnitsSold = data.UnitsSold
            });

        _reportBuilderServiceMock
            .Setup(x => x.BuildCategorySummaries(It.IsAny<Dictionary<string, CategoryData>>()))
            .Returns((Dictionary<string, CategoryData> categoryTotals) =>
                categoryTotals.Select(kvp => new GetMarginReportResponse.CategoryMarginSummary
                {
                    Category = kvp.Key,
                    TotalMargin = kvp.Value.TotalMargin,
                    TotalRevenue = kvp.Value.TotalRevenue,
                    ProductCount = kvp.Value.ProductCount,
                    TotalUnitsSold = kvp.Value.TotalUnitsSold,
                    AverageMarginPercentage = kvp.Value.TotalRevenue > 0 ? (kvp.Value.TotalMargin / kvp.Value.TotalRevenue) * 100 : 0
                }).ToList());

        _marginCalculationServiceMock
            .Setup(x => x.CalculateMarginPercentage(It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns((decimal margin, decimal revenue) => revenue > 0 ? (margin / revenue) * 100 : 0);
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
        result.TotalMargin.Should().Be(3000m); // 1500 + 1500
        result.TotalRevenue.Should().Be(4650m); // 2250 + 2400
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