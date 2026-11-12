using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<UpdateManufactureOrderHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IManufactureCatalogSource> _catalogSourceMock;
    private readonly UpdateManufactureOrderHandler _handler;

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);

    private const int ValidOrderId = 1;
    private const string ValidResponsiblePerson = "Jane Doe";
    private const string ValidLotNumber = "LOT-2024-001";
    private const string ValidNewNote = "Test note for update";

    public UpdateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _catalogSourceMock = new Mock<IManufactureCatalogSource>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object,
            _loggerMock.Object,
            _catalogSourceMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldUpdateOrderAndReturnResponse()
    {
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Order.Should().NotBeNull();
        result.Order!.Id.Should().Be(ValidOrderId);
        result.Order.ResponsiblePerson.Should().Be(ValidResponsiblePerson);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnResourceNotFoundError()
    {
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Params.Should().ContainKey("id");
        result.Params!["id"].Should().Be(ValidOrderId.ToString());
    }

    [Fact]
    public async Task Handle_ShouldUpdateBasicProperties()
    {
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();
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

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.PlannedDate.Should().Be(request.PlannedDate);
        updatedOrder.ResponsiblePerson.Should().Be(request.ResponsiblePerson);
    }

    [Fact]
    public async Task Handle_WithSemiProductUpdate_ShouldUpdateSemiProductProperties()
    {
        var request = CreateValidRequest();
        request.SemiProduct = new UpdateManufactureOrderSemiProductRequest
        {
            LotNumber = ValidLotNumber,
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30))
        };

        var existingOrder = CreateExistingOrder();
        // Planned date unchanged → the explicit lot/expiration is a manual override and is honoured.
        request.PlannedDate = existingOrder.PlannedDate;
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

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.SemiProduct.Should().NotBeNull();
        updatedOrder.SemiProduct!.LotNumber.Should().Be(ValidLotNumber);
        updatedOrder.SemiProduct.ExpirationDate.Should().Be(request.SemiProduct.ExpirationDate);
    }

    [Fact]
    public async Task Handle_ShouldReplaceAllProducts()
    {
        var request = CreateValidRequest();
        request.Products = new List<UpdateManufactureOrderProductRequest>
        {
            new()
            {
                ProductCode = "NEW-PROD-001",
                ProductName = "New Product 1",
                PlannedQuantity = 150.0
            },
            new()
            {
                ProductCode = "NEW-PROD-002",
                ProductName = "New Product 2",
                PlannedQuantity = 200.0
            }
        };

        var existingOrder = CreateExistingOrder();
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

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.Products.Should().HaveCount(2);

        var firstProduct = updatedOrder.Products.First(p => p.ProductCode == "NEW-PROD-001");
        firstProduct.ProductName.Should().Be("New Product 1");
        firstProduct.PlannedQuantity.Should().Be(150.0m);
        firstProduct.SemiProductCode.Should().Be("SEMI001");

        var secondProduct = updatedOrder.Products.First(p => p.ProductCode == "NEW-PROD-002");
        secondProduct.ProductName.Should().Be("New Product 2");
        secondProduct.PlannedQuantity.Should().Be(200.0m);
        secondProduct.SemiProductCode.Should().Be("SEMI001");
    }

    [Fact]
    public async Task Handle_WithNewNote_ShouldAddNoteToOrder()
    {
        var request = CreateValidRequest();
        request.NewNote = ValidNewNote;

        var existingOrder = CreateExistingOrder();
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

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.Notes.Should().HaveCount(1);

        var note = updatedOrder.Notes.First();
        note.Text.Should().Be(ValidNewNote);
        note.CreatedByUser.Should().Be("Test User");
        note.CreatedAt.Should().Be(FixedNow.UtcDateTime);
    }

    [Fact]
    public async Task Handle_WithEmptyNote_ShouldNotAddNote()
    {
        var request = CreateValidRequest();
        request.NewNote = "   "; // Whitespace only

        var existingOrder = CreateExistingOrder();
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

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder.Should().NotBeNull();
        updatedOrder!.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCallUpdateOrderOnRepository()
    {
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken ct) => order);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.UpdateOrderAsync(It.Is<ManufactureOrder>(o =>
                o.Id == ValidOrderId &&
                o.ResponsiblePerson == ValidResponsiblePerson),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRepositoryGetThrows_ShouldReturnInternalServerError()
    {
        var request = CreateValidRequest();

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
        var request = CreateValidRequest();
        var existingOrder = CreateExistingOrder();

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
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Error updating manufacture order {ValidOrderId}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldRefreshPlannedCatalogData()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingOrder());

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => order);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _catalogSourceMock.Verify(
            x => x.RefreshPlannedDataAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldNotRefreshPlannedCatalogData()
    {
        // Arrange
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _catalogSourceMock.Verify(
            x => x.RefreshPlannedDataAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithMetadataOnlyEdit_ShouldNotRefreshPlannedCatalogData()
    {
        // Arrange - a note/date edit leaves product quantities untouched, so the planned
        // totals the catalog caches cannot have changed.
        var request = new UpdateManufactureOrderRequest
        {
            Id = ValidOrderId,
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            ResponsiblePerson = ValidResponsiblePerson,
            NewNote = "Just a note"
        };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingOrder());

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => order);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _catalogSourceMock.Verify(
            x => x.RefreshPlannedDataAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPlannedDataRefreshFails_ShouldStillReportSuccess()
    {
        // Arrange - the order is already saved, so a cache hiccup must not surface
        // to the caller as a failed request.
        var request = CreateValidRequest();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingOrder());

        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) => order);

        _catalogSourceMock
            .Setup(x => x.RefreshPlannedDataAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache unavailable"));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Order.Should().NotBeNull();
    }

    // ── Lot / expiration recompute on planned-date change ────────────────────

    private static readonly DateOnly StoredPlannedDate = new(2026, 11, 2);
    private const string StoredLot = "4526";
    private static readonly DateOnly StoredExpiration = new(2028, 12, 31);
    private static readonly DateOnly NewPlannedDate = new(2026, 10, 30);
    private const string NewLot = "4426";
    private static readonly DateOnly NewExpiration = new(2029, 5, 31); // 30 months + 1, no day clamp

    [Theory]
    [InlineData(ManufactureOrderState.Draft)]
    [InlineData(ManufactureOrderState.Planned)]
    public async Task Handle_PlannedDateChangedBeforeSemiProductConfirmed_RecomputesLotAndExpirationEverywhere(
        ManufactureOrderState state)
    {
        var existingOrder = CreateOrderWithStoredLot(state);
        ManufactureOrder? updatedOrder = null;
        SetupRepository(existingOrder, o => updatedOrder = o);

        // What the UI sends: the new date plus the lot/expiration it last read from the server.
        var request = CreateLotRequest(NewPlannedDate, StoredLot, StoredExpiration);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        updatedOrder!.SemiProduct!.LotNumber.Should().Be(NewLot);
        updatedOrder.SemiProduct.ExpirationDate.Should().Be(NewExpiration);
        updatedOrder.Products.Should().AllSatisfy(p =>
        {
            p.LotNumber.Should().Be(NewLot);
            p.ExpirationDate.Should().Be(NewExpiration);
        });
        result.Order!.SemiProduct!.LotNumber.Should().Be(NewLot);
        result.Order.SemiProduct.ExpirationDate.Should().Be(NewExpiration);
    }

    [Fact]
    public async Task Handle_PlannedDateChangedTogetherWithManualLot_RecomputeWins()
    {
        var existingOrder = CreateOrderWithStoredLot(ManufactureOrderState.Planned);
        ManufactureOrder? updatedOrder = null;
        SetupRepository(existingOrder, o => updatedOrder = o);

        var request = CreateLotRequest(NewPlannedDate, "9999", new DateOnly(2030, 1, 31));

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder!.SemiProduct!.LotNumber.Should().Be(NewLot);
        updatedOrder.SemiProduct.ExpirationDate.Should().Be(NewExpiration);
    }

    [Theory]
    [InlineData(ManufactureOrderState.SemiProductManufactured)]
    [InlineData(ManufactureOrderState.Completed)]
    public async Task Handle_PlannedDateChangedAfterSemiProductConfirmed_KeepsStoredLotAndExpiration(
        ManufactureOrderState state)
    {
        var existingOrder = CreateOrderWithStoredLot(state);
        ManufactureOrder? updatedOrder = null;
        SetupRepository(existingOrder, o => updatedOrder = o);

        var request = CreateLotRequest(NewPlannedDate, StoredLot, StoredExpiration);

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder!.PlannedDate.Should().Be(NewPlannedDate);
        updatedOrder.SemiProduct!.LotNumber.Should().Be(StoredLot);
        updatedOrder.SemiProduct.ExpirationDate.Should().Be(StoredExpiration);
        updatedOrder.Products.Single().LotNumber.Should().Be(StoredLot);
        updatedOrder.Products.Single().ExpirationDate.Should().Be(StoredExpiration);
    }

    [Fact]
    public async Task Handle_PlannedDateUnchangedWithManualLot_HonoursOverrideAndPropagatesToProducts()
    {
        var existingOrder = CreateOrderWithStoredLot(ManufactureOrderState.Planned);
        ManufactureOrder? updatedOrder = null;
        SetupRepository(existingOrder, o => updatedOrder = o);

        var manualExpiration = new DateOnly(2028, 11, 30);
        var request = CreateLotRequest(StoredPlannedDate, "4426", manualExpiration);
        request.Products = new List<UpdateManufactureOrderProductRequest>(); // no product rows sent

        await _handler.Handle(request, CancellationToken.None);

        updatedOrder!.SemiProduct!.LotNumber.Should().Be("4426");
        updatedOrder.SemiProduct.ExpirationDate.Should().Be(manualExpiration);
        updatedOrder.Products.Single().LotNumber.Should().Be("4426");
        updatedOrder.Products.Single().ExpirationDate.Should().Be(manualExpiration);
    }

    private void SetupRepository(ManufactureOrder existingOrder, Action<ManufactureOrder> onUpdate)
    {
        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);
        _repositoryMock
            .Setup(x => x.UpdateOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder order, CancellationToken _) =>
            {
                onUpdate(order);
                return order;
            });
    }

    private static UpdateManufactureOrderRequest CreateLotRequest(DateOnly plannedDate, string lot, DateOnly expiration)
    {
        return new UpdateManufactureOrderRequest
        {
            Id = ValidOrderId,
            PlannedDate = plannedDate,
            SemiProduct = new UpdateManufactureOrderSemiProductRequest
            {
                LotNumber = lot,
                ExpirationDate = expiration,
            },
            Products = new List<UpdateManufactureOrderProductRequest>
            {
                new() { Id = 1, ProductCode = "ORIGINAL-PROD", ProductName = "Original Product", PlannedQuantity = 50.0 }
            }
        };
    }

    private static ManufactureOrder CreateOrderWithStoredLot(ManufactureOrderState state)
    {
        var order = CreateExistingOrder();
        order.PlannedDate = StoredPlannedDate;
        order.InitializeState(state, DateTime.UtcNow.AddDays(-1), "Original User");
        order.SemiProduct!.ExpirationMonths = 30;
        order.SemiProduct.LotNumber = StoredLot;
        order.SemiProduct.ExpirationDate = StoredExpiration;
        foreach (var product in order.Products)
        {
            product.LotNumber = StoredLot;
            product.ExpirationDate = StoredExpiration;
        }
        return order;
    }

    private static UpdateManufactureOrderRequest CreateValidRequest()
    {
        return new UpdateManufactureOrderRequest
        {
            Id = ValidOrderId,
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            ResponsiblePerson = ValidResponsiblePerson,
            Products = new List<UpdateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD001",
                    ProductName = "Updated Product",
                    PlannedQuantity = 100.0
                }
            }
        };
    }

    private static ManufactureOrder CreateExistingOrder()
    {
        var order = new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            CreatedByUser = "Original User",
            ResponsiblePerson = "Original Person",
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5))
        };
        order.InitializeState(ManufactureOrderState.Draft, DateTime.UtcNow.AddDays(-1), "Original User");

        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            Id = 1,
            ProductCode = "SEMI001",
            ProductName = "Semi Product 1",
            PlannedQuantity = 1000m,
            ActualQuantity = 1000m,
            LotNumber = null,
            ExpirationDate = null
        };

        order.Products.Add(new ManufactureOrderProduct
        {
            Id = 1,
            ProductCode = "ORIGINAL-PROD",
            ProductName = "Original Product",
            PlannedQuantity = 50m,
            ActualQuantity = 0m,
            SemiProductCode = "SEMI001"
        });

        return order;
    }
}