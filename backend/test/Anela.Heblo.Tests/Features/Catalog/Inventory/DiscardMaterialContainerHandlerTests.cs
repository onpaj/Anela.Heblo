using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class DiscardMaterialContainerHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly DiscardMaterialContainerHandler _handler;

    public DiscardMaterialContainerHandlerTests()
    {
        _handler = new DiscardMaterialContainerHandler(NullLogger<DiscardMaterialContainerHandler>.Instance, _containerRepo.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ExistingContainer_DiscardsSuccessfully()
    {
        // Arrange
        var container = new MaterialContainer("INT-00000001", "MAT001", "L1", 25m, "kg", "user");
        _containerRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(container);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "alice", null, true));

        // Act
        var result = await _handler.Handle(new DiscardMaterialContainerRequest { Id = 1 }, default);

        // Assert
        Assert.True(result.Success);
        _containerRepo.Verify(r => r.DeleteAsync(It.IsAny<MaterialContainer>(), default), Times.Never);
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

    [Fact]
    public async Task Handle_ExistingContainer_FlipsStatusToDiscardedAndSetsAudit()
    {
        // Arrange
        var container = new MaterialContainer("M00000001", "MAT001", "L1", null, null, "alice");
        _containerRepo.Setup(r => r.GetByIdAsync(7, default)).ReturnsAsync(container);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u2", "bob", null, true));

        // Act
        var result = await _handler.Handle(new DiscardMaterialContainerRequest { Id = 7 }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(MaterialContainerStatus.Discarded, container.Status);
        Assert.Equal("bob", container.UpdatedBy);
        Assert.NotNull(container.UpdatedAt);
        _containerRepo.Verify(r => r.DeleteAsync(It.IsAny<MaterialContainer>(), default), Times.Never);
    }
}
