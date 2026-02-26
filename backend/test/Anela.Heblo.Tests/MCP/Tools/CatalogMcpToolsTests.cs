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
}
