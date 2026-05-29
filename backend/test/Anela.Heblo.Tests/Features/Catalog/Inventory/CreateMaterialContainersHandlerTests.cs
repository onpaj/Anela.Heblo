using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class CreateMaterialContainersHandlerTests
{
    private readonly Mock<IMaterialContainerRepository> _containerRepo = new();
    private readonly Mock<IMaterialContainerCodeGenerator> _generator = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IPurchaseOrderRepository> _poRepo = new();
    private readonly CreateMaterialContainersHandler _handler;

    public CreateMaterialContainersHandlerTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Test User", null, true));
        _handler = new CreateMaterialContainersHandler(
            NullLogger<CreateMaterialContainersHandler>.Instance,
            _containerRepo.Object,
            _generator.Object,
            _currentUser.Object,
            _poRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesAndPersistsMaterialContainers()
    {
        // Arrange
        _generator.Setup(g => g.GenerateAsync(2, default))
            .ReturnsAsync(new List<string> { "INT-00000001", "INT-00000002" });
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .ReturnsAsync((IEnumerable<MaterialContainer> containers, CancellationToken _) => containers);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(2);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" },
                new() { MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" }
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
        _generator.Setup(g => g.GenerateAsync(1, default)).ReturnsAsync(new List<string> { "INT-00000001" }.AsReadOnly() as IReadOnlyList<string>);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        await _handler.Handle(request, default);

        // Assert
        Assert.NotNull(captured);
        Assert.Single(captured);
        Assert.Equal(MaterialContainerStatus.Assigned, captured[0].Status);
    }

    [Fact]
    public async Task Handle_ItemWithoutAmountOrUnit_CreatesContainer()
    {
        // Arrange
        _generator.Setup(g => g.GenerateAsync(1, default))
            .ReturnsAsync(new List<string> { "INT-00000001" }.AsReadOnly() as IReadOnlyList<string>);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "L1" } // No Amount, no Unit
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Null(captured[0].Amount);
        Assert.Null(captured[0].Unit);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsMaterialCodeAndLotCodeStrings()
    {
        // Arrange
        _generator.Setup(g => g.GenerateAsync(1, default))
            .ReturnsAsync(new List<string> { "INT-00000001" }.AsReadOnly() as IReadOnlyList<string>);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "SUPP-LOT-2026-04", Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal("MAT001", captured[0].MaterialCode);
        Assert.Equal("SUPP-LOT-2026-04", captured[0].LotCode);
    }

    [Fact]
    public async Task Handle_ItemWithPurchaseOrderLineId_PersistsLink()
    {
        // Arrange
        var line = new PurchaseOrderLine(1, "MAT001", "Material One", 10m, 5m, null);
        _poRepo.Setup(r => r.GetLineByIdAsync(42, default)).ReturnsAsync(line);
        _generator.Setup(g => g.GenerateAsync(1, default))
            .ReturnsAsync(new List<string> { "INT-00000001" }.AsReadOnly() as IReadOnlyList<string>);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "L1", PurchaseOrderLineId = 42 }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(42, captured![0].PurchaseOrderLineId);
    }

    [Fact]
    public async Task Handle_ItemWithNonExistentPurchaseOrderLineId_ReturnsError()
    {
        // Arrange
        _poRepo.Setup(r => r.GetLineByIdAsync(99, default)).ReturnsAsync((PurchaseOrderLine?)null);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "L1", PurchaseOrderLineId = 99 }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        _containerRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_ItemWithNoPurchaseOrderLineId_DoesNotValidatePo()
    {
        // Arrange
        _generator.Setup(g => g.GenerateAsync(1, default))
            .ReturnsAsync(new List<string> { "INT-00000001" }.AsReadOnly() as IReadOnlyList<string>);
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { MaterialCode = "MAT001", LotCode = "L1" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _poRepo.Verify(r => r.GetLineByIdAsync(It.IsAny<int>(), default), Times.Never);
    }
}
