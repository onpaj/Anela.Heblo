using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class CreateMaterialContainersHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IMaterialContainerCodeGenerator> _generator = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly CreateMaterialContainersHandler _handler;

    public CreateMaterialContainersHandlerTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Test User", null, true));
        _handler = new CreateMaterialContainersHandler(
            NullLogger<CreateMaterialContainersHandler>.Instance,
            _containerRepo.Object,
            _lotRepo.Object,
            _generator.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesAndPersistsMaterialContainers()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _generator.Setup(g => g.GenerateAsync(2, default))
            .ReturnsAsync(new List<string> { "INT-00000001", "INT-00000002" });
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .ReturnsAsync((IEnumerable<MaterialContainer> containers, CancellationToken _) => containers);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(2);

        var request = new CreateMaterialContainersRequest
        {
            LotId = 1,
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Amount = 25m, Unit = "kg" },
                new() { Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Containers.Count);
        Assert.Equal("INT-00000001", result.Containers[0].Code);
        Assert.Equal("INT-00000002", result.Containers[1].Code);
        _generator.Verify(g => g.GenerateAsync(2, default), Times.Once);
        _containerRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesContainersWithAssignedStatus()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _generator.Setup(g => g.GenerateAsync(1, default)).ReturnsAsync(new List<string> { "INT-00000001" }.AsReadOnly() as IReadOnlyList<string>);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            LotId = 1,
            Items = new List<CreateMaterialContainerItem> { new() { Amount = 25m, Unit = "kg" } }
        };

        // Act
        await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(captured);
        Assert.Single(captured);
        Assert.Equal(MaterialContainerStatus.Assigned, captured[0].Status);
    }

    [Fact]
    public async Task Handle_LotNotFound_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        var request = new CreateMaterialContainersRequest
        {
            LotId = 99,
            Items = new List<CreateMaterialContainerItem> { new() { Amount = 1m, Unit = "kg" } }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
        _generator.Verify(g => g.GenerateAsync(It.IsAny<int>(), default), Times.Never);
    }
}
