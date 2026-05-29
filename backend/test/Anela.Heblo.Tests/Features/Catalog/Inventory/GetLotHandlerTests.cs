using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly GetLotHandler _handler;

    public GetLotHandlerTests()
    {
        _handler = new GetLotHandler(NullLogger<GetLotHandler>.Instance, _lotRepo.Object, _containerRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingLot_ReturnsDtoWithContainers()
    {
        // Arrange
        var lot = new Lot("MAT001", "LOT-A", new DateOnly(2027, 1, 1), new DateOnly(2026, 5, 13), null, "user");
        _lotRepo.Setup(r => r.GetByIdWithEansAsync(1, default)).ReturnsAsync(lot);

        var containers = new PagedResult<MaterialContainer>
        {
            Items = new List<MaterialContainer>
            {
                new MaterialContainer("INT-00000001", "MAT001", "LOT-A", 25m, "kg", "user"),
                new MaterialContainer("INT-00000002", "MAT001", "LOT-A", 25m, "kg", "user")
            },
            TotalCount = 2,
            PageNumber = 1,
            PageSize = 100
        };
        _containerRepo.Setup(r => r.GetPaginatedAsync("MAT001", "LOT-A", 1, 100, default)).ReturnsAsync(containers);

        // Act
        var result = await _handler.Handle(new GetLotRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
        Assert.Equal(2, result.Containers.Count);
        Assert.Equal("INT-00000001", result.Containers[0].Code);
    }

    [Fact]
    public async Task Handle_MissingLot_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdWithEansAsync(99, default)).ReturnsAsync((Lot?)null);

        // Act
        var result = await _handler.Handle(new GetLotRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
    }
}
