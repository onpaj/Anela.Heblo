using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListMaterialContainers;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class ListMaterialContainersHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly ListMaterialContainersHandler _handler;

    public ListMaterialContainersHandlerTests()
    {
        _handler = new ListMaterialContainersHandler(NullLogger<ListMaterialContainersHandler>.Instance, _containerRepo.Object);
    }

    [Fact]
    public async Task Handle_FilterByMaterialCodeAndLotCode_DelegatesToRepository()
    {
        // Arrange
        var paged = new PagedResult<MaterialContainer>
        {
            Items = new List<MaterialContainer> { new MaterialContainer("INT-00000001", "MAT001", "L1", 25m, "kg", "user") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };
        _containerRepo.Setup(r => r.GetPaginatedAsync("MAT001", "L1", null, 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListMaterialContainersRequest { MaterialCode = "MAT001", LotCode = "L1", Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Containers);
    }

    [Fact]
    public async Task Handle_FilterByCode_PassesCodeToRepository()
    {
        // Arrange
        var paged = new PagedResult<MaterialContainer>
        {
            Items = new List<MaterialContainer> { new MaterialContainer("M00001234", "MAT001", "L1", 25m, "kg", "user") },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20
        };
        _containerRepo.Setup(r => r.GetPaginatedAsync(null, null, "M00001234", 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListMaterialContainersRequest { Code = "M00001234", Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Containers);
        Assert.Equal("M00001234", result.Containers[0].Code);
        _containerRepo.Verify(r => r.GetPaginatedAsync(null, null, "M00001234", 1, 20, default), Times.Once);
    }
}
