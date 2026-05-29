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
        _containerRepo.Setup(r => r.GetPaginatedAsync("MAT001", "L1", 1, 20, default)).ReturnsAsync(paged);

        // Act
        var result = await _handler.Handle(new ListMaterialContainersRequest { MaterialCode = "MAT001", LotCode = "L1", Page = 1, PageSize = 20 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Containers);
    }
}
