using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

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
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetCatalogListResponse>(okResult.Value);

        Assert.Equal(expectedResponse.TotalCount, response.TotalCount);
        Assert.Equal(expectedResponse.PageNumber, response.PageNumber);
        Assert.Equal(expectedResponse.PageSize, response.PageSize);
        Assert.Single(response.Items);

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
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetCatalogListResponse>(okResult.Value);

        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);

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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        Assert.IsType<NoContentResult>(result);
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
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetCatalogListResponse>(okResult.Value);

        Assert.Equal(pageNumber, response.PageNumber);
        Assert.Equal(pageSize, response.PageSize);

        _mediatorMock.Verify(m => m.Send(It.IsAny<GetCatalogListRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

}