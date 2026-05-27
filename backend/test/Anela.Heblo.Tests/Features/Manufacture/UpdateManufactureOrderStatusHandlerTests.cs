using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderStatusHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IConditionsReadingProvider> _conditionsProviderMock;
    private readonly UpdateManufactureOrderStatusHandler _handler;

    private const int ValidOrderId = 1;
    private const int NonExistentOrderId = 999;
    private const string TestUserName = "Test User";
    private const string ValidChangeReason = "Moving to next phase";

    public UpdateManufactureOrderStatusHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _conditionsProviderMock = new Mock<IConditionsReadingProvider>();

        _catalogRepositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, CatalogAggregate>());

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "test-id",
                Name: TestUserName,
                Email: "test@example.com",
                IsAuthenticated: true));

        _conditionsProviderMock
            .Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable));

        _inventoryRepositoryMock
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<ManufacturedProductInventoryItem> items, CancellationToken _) => items);

        _handler = new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _conditionsProviderMock.Object,
            _inventoryRepositoryMock.Object,
            _catalogRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidTransition_ShouldUpdateStateAndReturnResponse()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned,
            ChangeReason = ValidChangeReason
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OldState.Should().Be("Draft");
        result.NewState.Should().Be("Planned");
        result.StateChangedByUser.Should().Be(TestUserName);
        result.StateChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));

        updatedOrder.Should().NotBeNull();
        updatedOrder!.State.Should().Be(ManufactureOrderState.Planned);
        updatedOrder.StateChangedByUser.Should().Be(TestUserName);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnResourceNotFoundError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = NonExistentOrderId,
            NewState = ManufactureOrderState.Planned
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(NonExistentOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Params.Should().ContainKey("id");
        result.Params!["id"].Should().Be(NonExistentOrderId.ToString());
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Draft)] // Same state
    [InlineData(ManufactureOrderState.Cancelled, ManufactureOrderState.Draft)] // Cannot change from cancelled
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Completed)] // Skip states
    public async Task Handle_WithInvalidTransition_ShouldReturnInvalidOperationError(
        ManufactureOrderState fromState, ManufactureOrderState toState)
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = toState
        };

        var existingOrder = CreateOrderInState(fromState);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.Params.Should().ContainKey("oldState");
        result.Params.Should().ContainKey("newState");
        result.Params!["oldState"].Should().Be(fromState.ToString());
        result.Params["newState"].Should().Be(toState.ToString());
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed)]
    [InlineData(ManufactureOrderState.Completed, ManufactureOrderState.Cancelled)]
    public async Task Handle_WithValidTransitions_ShouldSucceed(
        ManufactureOrderState fromState, ManufactureOrderState toState)
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = toState
        };

        var existingOrder = CreateOrderInState(fromState);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OldState.Should().Be(fromState.ToString());
        result.NewState.Should().Be(toState.ToString());
    }



    [Fact]
    public async Task Handle_UnauthenticatedUser_ShouldUseSystemAsUser()
    {
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: null,
                Name: null,
                Email: null,
                IsAuthenticated: false));

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StateChangedByUser.Should().Be("System");

        updatedOrder.Should().NotBeNull();
        updatedOrder!.StateChangedByUser.Should().Be("System");
    }

    [Fact]
    public async Task Handle_AuthenticatedUserWithoutNameClaim_ShouldRecordUnknownUserNotSystem()
    {
        // Entra ID access tokens frequently omit the Name/upn claim used by Identity.Name.
        // Before the refactor this case fell through to "System" (the bug). Per spec FR-4
        // (Amendment 1a), the handler should now stamp "Unknown User" for an authenticated
        // principal whose Name is null.
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: "abc123",
                Name: null,
                Email: "user@example.com",
                IsAuthenticated: true));

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StateChangedByUser.Should().Be("Unknown User");
        result.StateChangedByUser.Should().NotBe("System");

        updatedOrder.Should().NotBeNull();
        updatedOrder!.StateChangedByUser.Should().Be("Unknown User");
    }

    [Fact]
    public async Task Handle_WhenRepositoryGetThrows_ShouldReturnInternalServerError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_WhenRepositoryUpdateThrows_ShouldReturnInternalServerError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Draft);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Update failed"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_WhenExceptionOccurs_ShouldLogError()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Planned
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error updating manufacture order status for order {ValidOrderId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenWeightFieldsProvided_SetsThemOnSavedOrder()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Completed,
            WeightWithinTolerance = true,
            WeightDifference = 5.5m
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        _repositoryMock.Verify(x => x.UpdateOrderAsync(
            It.Is<ManufactureOrder>(o =>
                o.WeightWithinTolerance == true &&
                o.WeightDifference == 5.5m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenWeightFieldsNull_DoesNotOverwriteExistingValues()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.SemiProductManufactured,
            WeightWithinTolerance = null,
            WeightDifference = null
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Planned);
        existingOrder.WeightWithinTolerance = true;
        existingOrder.WeightDifference = 3.0m;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.UpdateOrderAsync(
            It.Is<ManufactureOrder>(o =>
                o.WeightWithinTolerance == true &&
                o.WeightDifference == 3.0m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPhaseAFlexiDocCodesProvided_PersistsThem()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.SemiProductManufactured,
            FlexiDocMaterialIssueForSemiProduct = "V-MAT-001",
            FlexiDocSemiProductReceipt = "V-POL-001",
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.Planned);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        updatedOrder!.DocMaterialIssueForSemiProduct.Should().Be("V-MAT-001");
        updatedOrder.DocMaterialIssueForSemiProductDate.Should().NotBeNull();
        updatedOrder.DocSemiProductReceipt.Should().Be("V-POL-001");
        updatedOrder.DocSemiProductReceiptDate.Should().NotBeNull();
        updatedOrder.DocSemiProductIssueForProduct.Should().BeNull();
        updatedOrder.DocProductReceipt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenPhaseBFlexiDocCodesProvided_PersistsThem()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Completed,
            FlexiDocSemiProductIssueForProduct = "V-POLV-001",
            FlexiDocMaterialIssueForProduct = "V-MATV-001",
            FlexiDocProductReceipt = "V-PRIJEM-001",
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        ManufactureOrder? updatedOrder = null;

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) =>
            {
                updatedOrder = order;
                return order;
            });

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        updatedOrder!.DocSemiProductIssueForProduct.Should().Be("V-POLV-001");
        updatedOrder.DocSemiProductIssueForProductDate.Should().NotBeNull();
        updatedOrder.DocMaterialIssueForProduct.Should().Be("V-MATV-001");
        updatedOrder.DocMaterialIssueForProductDate.Should().NotBeNull();
        updatedOrder.DocProductReceipt.Should().Be("V-PRIJEM-001");
        updatedOrder.DocProductReceiptDate.Should().NotBeNull();
        updatedOrder.DocMaterialIssueForSemiProduct.Should().BeNull();
        updatedOrder.DocSemiProductReceipt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenFlexiDocCodesNull_DoesNotOverwriteExistingValues()
    {
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Completed,
            FlexiDocMaterialIssueForSemiProduct = null,
        };

        var existingOrder = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        existingOrder.DocMaterialIssueForSemiProduct = "V-MAT-EXISTING";

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.UpdateOrderAsync(
            It.Is<ManufactureOrder>(o => o.DocMaterialIssueForSemiProduct == "V-MAT-EXISTING"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TransitionToCompleted_CreatesInventoryItemsForFinishedProducts()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001",
                ProductName = "Product One",
                ActualQuantity = 10m,
                LotNumber = "LOT-A",
                ExpirationDate = new DateOnly(2027, 6, 1),
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-002",
                ProductName = "Product Two",
                ActualQuantity = 5m,
                LotNumber = null,
                ExpirationDate = null,
                ManufactureOrderId = ValidOrderId
            }
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder o, CancellationToken _) => o);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Completed
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<ManufacturedProductInventoryItem>>(items =>
                    items.Any(i =>
                        i.ProductCode == "PROD-001" &&
                        i.Amount == 10m &&
                        i.ManufactureOrderId == ValidOrderId &&
                        i.CreatedBy == TestUserName) &&
                    items.Any(i =>
                        i.ProductCode == "PROD-002" &&
                        i.Amount == 5m &&
                        i.ManufactureOrderId == ValidOrderId &&
                        i.CreatedBy == TestUserName) &&
                    items.Count() == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TransitionToCompleted_SkipsProductsWithZeroActualQuantity()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-ZERO",
                ProductName = "Zero Quantity",
                ActualQuantity = 0m,
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-NULL",
                ProductName = "Null Quantity",
                ActualQuantity = null,
                ManufactureOrderId = ValidOrderId
            },
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-OK",
                ProductName = "Has Quantity",
                ActualQuantity = 3m,
                ManufactureOrderId = ValidOrderId
            }
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder o, CancellationToken _) => o);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.Completed
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<ManufacturedProductInventoryItem>>(items =>
                    items.Single().ProductCode == "PROD-OK"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TransitionFromCompleted_DoesNotTouchInventory()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Completed);
        order.Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "PROD-001",
                ProductName = "Product One",
                ActualQuantity = 10m,
                ManufactureOrderId = ValidOrderId
            }
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder o, CancellationToken _) => o);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = ValidOrderId,
            NewState = ManufactureOrderState.SemiProductManufactured
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        _inventoryRepositoryMock.Verify(
            r => r.AddRangeAsync(It.IsAny<IEnumerable<ManufacturedProductInventoryItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ManufactureOrder CreateOrderInState(ManufactureOrderState state)
    {
        return new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            CreatedByUser = "Original User",
            ResponsiblePerson = "Test Person",
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            State = state,
            StateChangedAt = DateTime.UtcNow.AddDays(-1),
            StateChangedByUser = "Original User"
        };
    }
}