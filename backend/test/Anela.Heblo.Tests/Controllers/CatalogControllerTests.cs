using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.RefreshData;
using FluentAssertions;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using MediatR;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Moq;
using FluentAssertions;

namespace Anela.Heblo.Tests.Controllers;

public class CatalogControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<CatalogController>> _loggerMock;
    private readonly CatalogController _controller;

    public CatalogControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<CatalogController>>();
        _controller = new CatalogController(_mediatorMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task GetCatalogList_Should_Return_Ok_With_Response()
    {
        // Arrange
        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10,
            ProductName = "test"
        };

        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>
            {
                new CatalogItemDto
                {
                    ProductCode = "TEST001",
                    ProductName = "Test Product",
                    Type = ProductType.Material,
                    Location = "A1",
                    Stock = new StockDto
                    {
                        Available = 100,
                        Erp = 50,
                        Eshop = 30
                    }
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 10
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetCatalogList(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCatalogListResponse>();

        response.Subject.TotalCount.Should().Be(expectedResponse.TotalCount);
        response.Subject.PageNumber.Should().Be(expectedResponse.PageNumber);
        response.Subject.PageSize.Should().Be(expectedResponse.PageSize);
        response.Subject.Items.Should().HaveCount(1);

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCatalogList_Should_Handle_Empty_Response()
    {
        // Arrange
        var request = new GetCatalogListRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>(),
            TotalCount = 0,
            PageNumber = 1,
            PageSize = 10
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetCatalogList(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCatalogListResponse>();

        response.Subject.TotalCount.Should().Be(0);
        response.Subject.Items.Should().BeEmpty();

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshTransportData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshTransportDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshTransportData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshTransportDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshReserveData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshReserveDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshReserveData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshReserveDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshSalesData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshSalesDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshSalesData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshSalesDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAttributesData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshAttributesDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshAttributesData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshAttributesDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshErpStockData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshErpStockDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshErpStockData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshErpStockDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshEshopStockData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshEshopStockDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshEshopStockData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshEshopStockDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshPurchaseHistoryData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshPurchaseHistoryDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshPurchaseHistoryData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshPurchaseHistoryDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshConsumedHistoryData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshConsumedHistoryDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshConsumedHistoryData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshConsumedHistoryDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshStockTakingData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshStockTakingDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshStockTakingData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshStockTakingDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshLotsData_Should_Return_NoContent()
    {
        // Arrange
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RefreshLotsDataRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.RefreshLotsData();

        // Assert
        result.Should().BeOfType<NoContentResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<RefreshLotsDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, 10, null, null, null)]
    [InlineData(2, 20, "test product", null, null)]
    [InlineData(1, 5, null, "TEST001", null)]
    [InlineData(1, 10, "product", "CODE", ProductType.Material)]
    public async Task GetCatalogList_Should_Handle_Various_Parameters(
        int pageNumber,
        int pageSize,
        string? productName,
        string? productCode,
        ProductType? type)
    {
        // Arrange
        var request = new GetCatalogListRequest
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            ProductName = productName,
            ProductCode = productCode,
            Type = type
        };

        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>(),
            TotalCount = 0,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetCatalogListRequest>(r =>
                r.PageNumber == pageNumber &&
                r.PageSize == pageSize &&
                r.ProductName == productName &&
                r.ProductCode == productCode &&
                r.Type == type), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetCatalogList(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCatalogListResponse>();

        response.Subject.PageNumber.Should().Be(pageNumber);
        response.Subject.PageSize.Should().Be(pageSize);

        _mediatorMock.Verify(m => m.Send(It.IsAny<GetCatalogListRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsForAutocomplete_Should_Return_Ok_With_Filtered_Results()
    {
        // Arrange
        var searchTerm = "test";
        var limit = 10;

        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>
            {
                new CatalogItemDto
                {
                    ProductCode = "TEST001",
                    ProductName = "Test Product",
                    Type = ProductType.Material,
                    Location = "A1",
                    Stock = new StockDto
                    {
                        Available = 100,
                        Erp = 50,
                        Eshop = 30
                    }
                },
                new CatalogItemDto
                {
                    ProductCode = "TEST002",
                    ProductName = "Another Test",
                    Type = ProductType.Product,
                    Location = "B2",
                    Stock = new StockDto
                    {
                        Available = 50,
                        Erp = 25,
                        Eshop = 15
                    }
                }
            },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 10
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetCatalogListRequest>(r =>
                r.SearchTerm == searchTerm &&
                r.PageSize == limit &&
                r.PageNumber == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetProductsForAutocomplete(searchTerm, limit);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCatalogListResponse>();

        response.Subject.Items.Count.Should().Be(2);
        response.Subject.TotalCount.Should().Be(2);
        response.Subject.Items[0].ProductCode.Should().Be("TEST001");
        response.Subject.Items[0].ProductName.Should().Be("Test Product");

        _mediatorMock.Verify(m => m.Send(It.IsAny<GetCatalogListRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsForAutocomplete_Should_Use_Default_Limit_When_Not_Provided()
    {
        // Arrange
        var searchTerm = "test";

        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>(),
            TotalCount = 0,
            PageNumber = 1,
            PageSize = 20 // Default limit
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetCatalogListRequest>(r =>
                r.SearchTerm == searchTerm &&
                r.PageSize == 20 && // Default limit
                r.PageNumber == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetProductsForAutocomplete(searchTerm);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCatalogListResponse>();

        response.Subject.PageSize.Should().Be(20);

        _mediatorMock.Verify(m => m.Send(It.Is<GetCatalogListRequest>(r => r.PageSize == 20), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetProductsForAutocomplete_Should_Handle_Null_Search_Term()
    {
        // Arrange
        string? searchTerm = null;
        var limit = 5;

        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>(),
            TotalCount = 0,
            PageNumber = 1,
            PageSize = 5
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetCatalogListRequest>(r =>
                r.ProductName == null &&
                r.ProductCode == null &&
                r.PageSize == limit &&
                r.PageNumber == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetProductsForAutocomplete(searchTerm, limit);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCatalogListResponse>();

        response.Subject.TotalCount.Should().Be(0);
        response.Subject.PageSize.Should().Be(5);

        _mediatorMock.Verify(m => m.Send(It.IsAny<GetCatalogListRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

}