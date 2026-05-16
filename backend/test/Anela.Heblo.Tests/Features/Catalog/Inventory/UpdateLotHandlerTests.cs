using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class UpdateLotHandlerTests
{
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly UpdateLotHandler _handler;

    public UpdateLotHandlerTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Test User", null, true));
        _handler = new UpdateLotHandler(NullLogger<UpdateLotHandler>.Instance, _lotRepo.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ExistingLot_UpdatesMutableFields()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, new DateOnly(2026, 5, 1), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _lotRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new UpdateLotRequest
        {
            Id = 1,
            Expiration = new DateOnly(2027, 12, 31),
            ReceivedDate = new DateOnly(2026, 5, 13),
            Notes = "Updated notes"
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(new DateOnly(2027, 12, 31), result.Lot.Expiration);
        Assert.Equal("Updated notes", result.Lot.Notes);
        _lotRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingLot_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        // Act
        var result = await _handler.Handle(new UpdateLotRequest { Id = 99, ReceivedDate = DateOnly.FromDateTime(DateTime.Today) }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
    }
}
