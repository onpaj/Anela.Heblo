using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class DiscardMaterialContainerHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly DiscardMaterialContainerHandler _handler;

    public DiscardMaterialContainerHandlerTests()
    {
        _handler = new DiscardMaterialContainerHandler(NullLogger<DiscardMaterialContainerHandler>.Instance, _containerRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingContainer_DeletesSuccessfully()
    {
        // Arrange
        var container = new MaterialContainer("INT-00000001", "MAT001", "L1", 25m, "kg", "user");
        _containerRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(container);

        // Act
        var result = await _handler.Handle(new DiscardMaterialContainerRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        _containerRepo.Verify(r => r.DeleteAsync(container, default), Times.Once);
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingContainer_ReturnsMaterialContainerNotFound()
    {
        // Arrange
        _containerRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((MaterialContainer?)null);

        // Act
        var result = await _handler.Handle(new DiscardMaterialContainerRequest { Id = 99 }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.MaterialContainerNotFound, result.ErrorCode);
    }
}
