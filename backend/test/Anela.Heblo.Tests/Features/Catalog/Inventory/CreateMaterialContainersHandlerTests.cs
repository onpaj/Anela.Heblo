using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;
using Anela.Heblo.Application.Shared;
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
            _currentUser.Object,
            _poRepo.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsContainersWithProvidedCodes()
    {
        // Arrange
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(2);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" },
                new() { Code = "M00000002", MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Containers.Count);
        Assert.Equal("M00000001", result.Containers[0].Code);
        Assert.Equal("M00000002", result.Containers[1].Code);
        _containerRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesContainersWithAssignedStatus()
    {
        // Arrange
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" }
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
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" } // No Amount, no Unit
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
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "SUPP-LOT-2026-04", Amount = 25m, Unit = "kg" }
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
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        var line = new PurchaseOrderLine(1, "MAT001", "Material One", 10m, 5m, null);
        _poRepo.Setup(r => r.GetLineByIdAsync(42, default)).ReturnsAsync(line);
        List<MaterialContainer>? captured = null;
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .Callback<IEnumerable<MaterialContainer>, CancellationToken>((items, _) => captured = items.ToList())
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1", PurchaseOrderLineId = 42 }
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
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        _poRepo.Setup(r => r.GetLineByIdAsync(99, default)).ReturnsAsync((PurchaseOrderLine?)null);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1", PurchaseOrderLineId = 99 }
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
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);
        _containerRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default))
            .ReturnsAsync((IEnumerable<MaterialContainer> items, CancellationToken _) => items);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        _poRepo.Verify(r => r.GetLineByIdAsync(It.IsAny<int>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidCodeFormat_ReturnsValidationError()
    {
        // Arrange
        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "BADCODE", MaterialCode = "MAT001", LotCode = "L1" }
            }
        };

        // Act
        var validator = new CreateMaterialContainersRequestValidator();
        var result = await validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Code") && e.ErrorMessage.Contains("M"));
    }

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsCodeExistsError()
    {
        // Arrange
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default))
            .ReturnsAsync(new MaterialContainer("M00000001", "MAT001", "L1", null, null, "user"));

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.MaterialContainerCodeExists, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_BatchWithDuplicateCodes_ReturnsCodeExistsError()
    {
        // Arrange
        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" },
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.MaterialContainerCodeExists, result.ErrorCode);
        _containerRepo.Verify(r => r.GetByCodeAsync(It.IsAny<string>(), default), Times.Never);
        _containerRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<MaterialContainer>>(), default), Times.Never);
    }
}
