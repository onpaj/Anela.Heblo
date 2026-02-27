using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetMaterialForPurchase;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetWarehouseStatistics;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class CatalogMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly CatalogMcpTools _tools;

    public CatalogMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new CatalogMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetCatalogList_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCatalogListResponse
        {
            Items = new List<CatalogItemDto>(),
            TotalCount = 0,
            PageNumber = 2,
            PageSize = 25
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetCatalogList(
            searchTerm: "Bisabolol",
            productTypes: new[] { ProductType.Material },
            pageNumber: 2,
            pageSize: 25
        );

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetCatalogListRequest>(req =>
                req.SearchTerm == "Bisabolol" &&
                req.ProductTypes != null &&
                req.ProductTypes.Length == 1 &&
                req.ProductTypes[0] == ProductType.Material &&
                req.PageNumber == 2 &&
                req.PageSize == 25
            ),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetCatalogListResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.TotalCount);
        Assert.Equal(2, deserialized.PageNumber);
        Assert.Equal(25, deserialized.PageSize);
    }

    [Fact]
    public async Task GetCatalogDetail_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCatalogDetailResponse
        {
            Success = true,
            Item = new CatalogItemDto { ProductCode = "AKL001" }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetCatalogDetail(
            productCode: "AKL001",
            monthsBack: 6
        );

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetCatalogDetailRequest>(req =>
                req.ProductCode == "AKL001" &&
                req.MonthsBack == 6
            ),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetCatalogDetailResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal("AKL001", deserialized.Item?.ProductCode);
    }

    [Fact]
    public async Task GetCatalogDetail_ShouldThrowInvalidOperationException_WhenProductNotFound()
    {
        // Arrange
        var errorResponse = new GetCatalogDetailResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ProductNotFound,
            Params = new Dictionary<string, string>
            {
                { "ProductCode", "XYZ123" }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetCatalogDetail("XYZ123")
        );

        Assert.Contains("ProductNotFound", exception.Message);
        Assert.Contains("XYZ123", exception.Message);
    }

    [Fact]
    public async Task GetProductComposition_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetProductCompositionResponse
        {
            Success = true
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetProductCompositionRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetProductComposition("AKL001");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetProductCompositionRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetProductCompositionResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task GetProductComposition_ShouldThrowMcpException_WhenProductNotFound()
    {
        // Arrange
        var errorResponse = new GetProductCompositionResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ProductNotFound,
            Params = new Dictionary<string, string>
            {
                { "ProductCode", "XYZ123" }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetProductCompositionRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetProductComposition("XYZ123")
        );

        Assert.Contains("ProductNotFound", exception.Message);
        Assert.Contains("XYZ123", exception.Message);
    }

    [Fact]
    public async Task GetMaterialsForPurchase_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetMaterialsForPurchaseResponse();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetMaterialsForPurchaseRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetMaterialsForPurchase();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetMaterialsForPurchaseRequest>(),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetMaterialsForPurchaseResponse>(jsonResult);
        Assert.NotNull(deserialized);
    }

    [Fact]
    public async Task GetAutocomplete_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCatalogListResponse();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogListRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetAutocomplete("Bis", 10, new[] { ProductType.Material });

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetCatalogListRequest>(req =>
                req.SearchTerm == "Bis" &&
                req.PageSize == 10 &&
                req.PageNumber == 1 &&
                req.ProductTypes != null &&
                req.ProductTypes.Length == 1 &&
                req.ProductTypes[0] == ProductType.Material
            ),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetCatalogListResponse>(jsonResult);
        Assert.NotNull(deserialized);
    }

    [Fact]
    public async Task GetProductUsage_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetProductUsageResponse
        {
            Success = true
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetProductUsageRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetProductUsage("AKL001");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetProductUsageRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetProductUsageResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task GetProductUsage_ShouldThrowMcpException_WhenProductNotFound()
    {
        // Arrange
        var errorResponse = new GetProductUsageResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ProductNotFound,
            Params = new Dictionary<string, string>
            {
                { "ProductCode", "XYZ123" }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetProductUsageRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetProductUsage("XYZ123")
        );

        Assert.Contains("ProductNotFound", exception.Message);
        Assert.Contains("XYZ123", exception.Message);
    }

    [Fact]
    public async Task GetWarehouseStatistics_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetWarehouseStatisticsResponse();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetWarehouseStatisticsRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetWarehouseStatistics();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetWarehouseStatisticsRequest>(),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetWarehouseStatisticsResponse>(jsonResult);
        Assert.NotNull(deserialized);
    }
}
