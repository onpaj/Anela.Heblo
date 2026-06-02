using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLastUsedLotForMaterial;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetLastUsedLotForMaterialHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _repo = new();
    private readonly GetLastUsedLotForMaterialHandler _handler;

    public GetLastUsedLotForMaterialHandlerTests()
    {
        _handler = new GetLastUsedLotForMaterialHandler(
            NullLogger<GetLastUsedLotForMaterialHandler>.Instance, _repo.Object);
    }

    [Fact]
    public async Task Handle_NoContainersForMaterial_ReturnsNullLotCode()
    {
        _repo.Setup(r => r.GetLastUsedLotCodeForMaterialAsync("MAT001", default))
            .ReturnsAsync((string?)null);

        var result = await _handler.Handle(
            new GetLastUsedLotForMaterialRequest { MaterialCode = "MAT001" }, default);

        Assert.True(result.Success);
        Assert.Null(result.LotCode);
    }

    [Fact]
    public async Task Handle_HasContainers_ReturnsMostRecentLotCode()
    {
        _repo.Setup(r => r.GetLastUsedLotCodeForMaterialAsync("MAT001", default))
            .ReturnsAsync("LOT-2026-04");

        var result = await _handler.Handle(
            new GetLastUsedLotForMaterialRequest { MaterialCode = "MAT001" }, default);

        Assert.True(result.Success);
        Assert.Equal("LOT-2026-04", result.LotCode);
    }
}
