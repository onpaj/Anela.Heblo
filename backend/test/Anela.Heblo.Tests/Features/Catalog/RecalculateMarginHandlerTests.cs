using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateMargin;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class RecalculateMarginHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMarginCalculationService> _marginCalculationServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<ILogger<RecalculateMarginHandler>> _loggerMock;
    private readonly RecalculateMarginHandler _handler;

    public RecalculateMarginHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _marginCalculationServiceMock = new Mock<IMarginCalculationService>();
        _timeProviderMock = new Mock<TimeProvider>();
        _loggerMock = new Mock<ILogger<RecalculateMarginHandler>>();

        _handler = new RecalculateMarginHandler(
            _catalogRepositoryMock.Object,
            _marginCalculationServiceMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task Handle_ValidProductCode_ReturnsSuccessWithMarginHistory()
    {
        // Arrange
        var productCode = "TEST001";
        var currentDate = new DateTime(2025, 12, 19);
        var request = new RecalculateMarginRequest
        {
            ProductCode = productCode,
            MonthsBack = 13
        };

        var catalogItem = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            EshopPrice = new ProductPriceEshop
            {
                PriceWithoutVat = 100,
                PriceWithVat = 121
            }
        };

        var marginHistory = new MonthlyMarginHistory
        {
            MonthlyData = new List<MonthlyMarginData>
            {
                new MonthlyMarginData
                {
                    Month = new DateTime(2025, 12, 1),
                    M0 = MarginLevel.Create(100, 30, 30),
                    M1_A = MarginLevel.Create(100, 40, 10),
                    M1_B = MarginLevel.Create(100, 40, 10),
                    M2 = MarginLevel.Create(100, 50, 10),
                    M3 = MarginLevel.Create(100, 60, 10)
                },
                new MonthlyMarginData
                {
                    Month = new DateTime(2025, 11, 1),
                    M0 = MarginLevel.Create(100, 32, 32),
                    M1_A = MarginLevel.Create(100, 42, 10),
                    M1_B = MarginLevel.Create(100, 42, 10),
                    M2 = MarginLevel.Create(100, 52, 10),
                    M3 = MarginLevel.Create(100, 62, 10)
                }
            }
        };

        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock.Setup(r => r.SingleOrDefaultAsync(
            It.IsAny<Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _marginCalculationServiceMock.Setup(s => s.GetMarginAsync(
            catalogItem,
            It.IsAny<IEnumerable<CatalogAggregate>>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(marginHistory);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MarginHistory);
        Assert.Equal(2, result.MarginHistory.Count);
        Assert.Equal(new DateTime(2025, 12, 1), result.MarginHistory[0].Date);
        Assert.Equal(new DateTime(2025, 11, 1), result.MarginHistory[1].Date);

        // Verify margin levels are correctly mapped
        Assert.Equal(70m, result.MarginHistory[0].M0.Percentage); // (100-30)/100 * 100 = 70%
        Assert.Equal(60m, result.MarginHistory[0].M1_A.Percentage); // (100-40)/100 * 100 = 60%
        Assert.Equal(50m, result.MarginHistory[0].M2.Percentage); // (100-50)/100 * 100 = 50%
        Assert.Equal(40m, result.MarginHistory[0].M3.Percentage); // (100-60)/100 * 100 = 40%
    }

    [Fact]
    public async Task Handle_ProductNotFound_ReturnsErrorResponse()
    {
        // Arrange
        var request = new RecalculateMarginRequest
        {
            ProductCode = "NONEXISTENT",
            MonthsBack = 13
        };

        _catalogRepositoryMock.Setup(r => r.SingleOrDefaultAsync(
            It.IsAny<Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate)null!);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ProductNotFound, result.ErrorCode);
        Assert.NotNull(result.Params);
        Assert.True(result.Params.ContainsKey("productCode"));
        Assert.Equal("NONEXISTENT", result.Params["productCode"]);
    }

    [Fact]
    public async Task Handle_MarginCalculationServiceThrows_ReturnsInternalServerError()
    {
        // Arrange
        var productCode = "TEST001";
        var currentDate = new DateTime(2025, 12, 19);
        var request = new RecalculateMarginRequest
        {
            ProductCode = productCode,
            MonthsBack = 13
        };

        var catalogItem = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            EshopPrice = new ProductPriceEshop
            {
                PriceWithoutVat = 100,
                PriceWithVat = 121
            }
        };

        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock.Setup(r => r.SingleOrDefaultAsync(
            It.IsAny<Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _marginCalculationServiceMock.Setup(s => s.GetMarginAsync(
            It.IsAny<CatalogAggregate>(),
            It.IsAny<IEnumerable<CatalogAggregate>>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InternalServerError, result.ErrorCode);
        Assert.NotNull(result.Params);
        Assert.True(result.Params.ContainsKey("error"));
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsMarginCalculationServiceWithCorrectDateRange()
    {
        // Arrange
        var productCode = "TEST001";
        var currentDate = new DateTime(2025, 12, 19);
        var expectedDateFrom = DateOnly.FromDateTime(currentDate.AddMonths(-13));
        var expectedDateTo = DateOnly.FromDateTime(currentDate);

        var request = new RecalculateMarginRequest
        {
            ProductCode = productCode,
            MonthsBack = 13
        };

        var catalogItem = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            EshopPrice = new ProductPriceEshop
            {
                PriceWithoutVat = 100,
                PriceWithVat = 121
            }
        };

        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock.Setup(r => r.SingleOrDefaultAsync(
            It.IsAny<Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _marginCalculationServiceMock.Setup(s => s.GetMarginAsync(
            It.IsAny<CatalogAggregate>(),
            It.IsAny<IEnumerable<CatalogAggregate>>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyMarginHistory());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _marginCalculationServiceMock.Verify(
            s => s.GetMarginAsync(
                catalogItem,
                It.IsAny<IEnumerable<CatalogAggregate>>(),
                expectedDateFrom,
                expectedDateTo,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CustomMonthsBack_UsesCorrectDateRange()
    {
        // Arrange
        var productCode = "TEST001";
        var currentDate = new DateTime(2025, 12, 19);
        var monthsBack = 24;
        var expectedDateFrom = DateOnly.FromDateTime(currentDate.AddMonths(-monthsBack));
        var expectedDateTo = DateOnly.FromDateTime(currentDate);

        var request = new RecalculateMarginRequest
        {
            ProductCode = productCode,
            MonthsBack = monthsBack
        };

        var catalogItem = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = "Test Product",
            EshopPrice = new ProductPriceEshop
            {
                PriceWithoutVat = 100,
                PriceWithVat = 121
            }
        };

        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock.Setup(r => r.SingleOrDefaultAsync(
            It.IsAny<Expression<Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _marginCalculationServiceMock.Setup(s => s.GetMarginAsync(
            It.IsAny<CatalogAggregate>(),
            It.IsAny<IEnumerable<CatalogAggregate>>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyMarginHistory());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _marginCalculationServiceMock.Verify(
            s => s.GetMarginAsync(
                catalogItem,
                It.IsAny<IEnumerable<CatalogAggregate>>(),
                expectedDateFrom,
                expectedDateTo,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
