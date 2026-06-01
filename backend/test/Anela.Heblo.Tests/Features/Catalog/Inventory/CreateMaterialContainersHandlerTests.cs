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
    public async Task Handle_UnassignedCode_AssignsContainerAndSaves()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);
        _containerRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(1);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1", Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(MaterialContainerStatus.Assigned, container.Status);
        Assert.Equal("MAT001", container.MaterialCode);
        Assert.Equal("L1", container.LotCode);
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_AssignsMultipleUnassignedContainers()
    {
        // Arrange
        var c1 = MaterialContainer.CreateUnassigned("M00000001", "admin");
        var c2 = MaterialContainer.CreateUnassigned("M00000002", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(c1);
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000002", default)).ReturnsAsync(c2);
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
        Assert.Equal(MaterialContainerStatus.Assigned, c1.Status);
        Assert.Equal(MaterialContainerStatus.Assigned, c2.Status);
    }

    [Fact]
    public async Task Handle_UnknownCode_ReturnsUnknownMaterialContainerCodeError()
    {
        // Arrange
        _containerRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), default)).ReturnsAsync((MaterialContainer?)null);

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
        Assert.Equal(ErrorCodes.UnknownMaterialContainerCode, result.ErrorCode);
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyAssignedCode_ReturnsCodeExistsErrorWithMaterialCode()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        container.Assign("MAT-OLD", "LOT-OLD", null, null, null, "worker");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);

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
        Assert.Equal("MAT-OLD", result.Params!["MaterialCode"]);
        Assert.Equal("LOT-OLD", result.Params!["LotCode"]);
        Assert.Equal(MaterialContainerStatus.Assigned.ToString(), result.Params!["Status"]);
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_DiscardedCode_ReturnsCodeExistsErrorWithDiscardedStatus()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        container.Discard("worker");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);

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
        Assert.Equal(MaterialContainerStatus.Discarded.ToString(), result.Params!["Status"]);
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_UnassignedCodeWithoutAmountOrUnit_Assigns()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);
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
        Assert.Null(container.Amount);
        Assert.Null(container.Unit);
    }

    [Fact]
    public async Task Handle_UnassignedCode_PersistsMaterialCodeAndLotCodeStrings()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);
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
        Assert.Equal("MAT001", container.MaterialCode);
        Assert.Equal("SUPP-LOT-2026-04", container.LotCode);
    }

    [Fact]
    public async Task Handle_ItemWithPurchaseOrderLineId_PersistsLink()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);
        var line = new PurchaseOrderLine(1, "MAT001", "Material One", 10m, 5m, null);
        _poRepo.Setup(r => r.GetLineByIdAsync(42, default)).ReturnsAsync(line);
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
        Assert.Equal(42, container.PurchaseOrderLineId);
    }

    [Fact]
    public async Task Handle_ItemWithNonExistentPurchaseOrderLineId_ReturnsError()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);
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
        Assert.Equal(ErrorCodes.PurchaseOrderLineNotFound, result.ErrorCode);
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task Handle_ItemWithNoPurchaseOrderLineId_DoesNotValidatePo()
    {
        // Arrange
        var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(container);
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
    public async Task Handle_MixedBatch_RejectsAtFirstBadItem_AndAssignsNothing()
    {
        // Arrange
        var containerA = MaterialContainer.CreateUnassigned("M00000001", "admin");
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", default)).ReturnsAsync(containerA);
        _containerRepo.Setup(r => r.GetByCodeAsync("M00000099", default)).ReturnsAsync((MaterialContainer?)null);

        var request = new CreateMaterialContainersRequest
        {
            Items = new List<CreateMaterialContainerItem>
            {
                new() { Code = "M00000001", MaterialCode = "MAT001", LotCode = "L1" },
                new() { Code = "M00000099", MaterialCode = "MAT001", LotCode = "L1" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UnknownMaterialContainerCode, result.ErrorCode);
        // Validate-all-before-mutate: A passed validation but B's null lookup
        // triggered an early return before the assignment loop ran.
        Assert.Equal(MaterialContainerStatus.Unassigned, containerA.Status);
        _containerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        _containerRepo.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }
}
