using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetMaterialContainerByCodeHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly GetMaterialContainerByCodeHandler _handler;

    public GetMaterialContainerByCodeHandlerTests()
    {
        _handler = new GetMaterialContainerByCodeHandler(NullLogger<GetMaterialContainerByCodeHandler>.Instance, _containerRepo.Object, _lotRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingCode_ReturnsContainerWithLot()
    {
        // Arrange
        var container = new MaterialContainer("INT-00000001", 1, 25m, "kg", "user");
        var lot = new Lot("MAT001", "L1", new DateOnly(2027, 6, 1), DateOnly.FromDateTime(DateTime.Today), null, "user");
        _containerRepo.Setup(r => r.GetByCodeAsync("INT-00000001", default)).ReturnsAsync(container);
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);

        // Act
        var result = await _handler.Handle(new GetMaterialContainerByCodeRequest { Code = "INT-00000001" }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("INT-00000001", result.Container.Code);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
    }

    [Fact]
    public async Task Handle_MissingCode_ReturnsMaterialContainerNotFound()
    {
        // Arrange
        _containerRepo.Setup(r => r.GetByCodeAsync("MISSING", default)).ReturnsAsync((MaterialContainer?)null);

        // Act
        var result = await _handler.Handle(new GetMaterialContainerByCodeRequest { Code = "MISSING" }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.MaterialContainerNotFound, result.ErrorCode);
    }
}
