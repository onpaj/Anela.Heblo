using Anela.Heblo.API.MCP;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
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
        var result = await _tools.GetCatalogList(
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

        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task GetCatalogDetail_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCatalogDetailResponse
        {
            Success = true,
            ProductCode = "AKL001"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetCatalogDetail(
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

        Assert.Equal(expectedResponse, result);
    }

    [Fact]
    public async Task GetCatalogDetail_ShouldThrowMcpToolException_WhenProductNotFound()
    {
        // Arrange
        var errorResponse = new GetCatalogDetailResponse("NOT_FOUND")
        {
            Success = false,
            ErrorMessage = "Product 'XYZ123' not found"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCatalogDetailRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpToolException>(
            () => _tools.GetCatalogDetail("XYZ123")
        );

        Assert.Equal("NOT_FOUND", exception.Code);
        Assert.Contains("XYZ123", exception.Message);
    }
}
