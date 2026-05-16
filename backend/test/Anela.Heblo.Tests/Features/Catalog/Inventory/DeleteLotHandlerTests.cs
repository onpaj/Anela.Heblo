using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class DeleteLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly DeleteLotHandler _handler;

    public DeleteLotHandlerTests()
    {
        _handler = new DeleteLotHandler(NullLogger<DeleteLotHandler>.Instance, _lotRepo.Object, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_LotWithNoEans_DeletesSuccessfully()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _eanRepo.Setup(r => r.AnyByLotIdAsync(1, default)).ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(new DeleteLotRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        _lotRepo.Verify(r => r.DeleteAsync(lot, default), Times.Once);
        _lotRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_LotWithEans_ReturnsLotHasEans()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _eanRepo.Setup(r => r.AnyByLotIdAsync(1, default)).ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(new DeleteLotRequest { Id = 1 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotHasEans, result.ErrorCode);
        _lotRepo.Verify(r => r.DeleteAsync(It.IsAny<Lot>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingLot_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        // Act
        var result = await _handler.Handle(new DeleteLotRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
    }
}
