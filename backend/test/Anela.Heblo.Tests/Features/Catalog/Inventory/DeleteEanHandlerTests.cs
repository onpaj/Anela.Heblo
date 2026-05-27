using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteEan;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class DeleteEanHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly DeleteEanHandler _handler;

    public DeleteEanHandlerTests()
    {
        _handler = new DeleteEanHandler(NullLogger<DeleteEanHandler>.Instance, _eanRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingEan_DeletesSuccessfully()
    {
        // Arrange
        var ean = new Ean("INT-00000001", 1, 25m, "kg", "user");
        _eanRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(ean);

        // Act
        var result = await _handler.Handle(new DeleteEanRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        _eanRepo.Verify(r => r.DeleteAsync(ean, default), Times.Once);
        _eanRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingEan_ReturnsEanNotFound()
    {
        // Arrange
        _eanRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Ean?)null);

        // Act
        var result = await _handler.Handle(new DeleteEanRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.EanNotFound, result.ErrorCode);
    }
}
