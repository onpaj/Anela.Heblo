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
    private readonly TimeProvider _timeProvider;
    private readonly UpdateManufactureOrderHandler _handler;

    private const int ValidOrderId = 1;
    private const string ValidResponsiblePerson = "Jane Doe";
    private const string ValidLotNumber = "LOT-2024-001";
    private const string ValidNewNote = "Test note for update";

    public UpdateManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderHandler>>();
        _timeProvider = TimeProvider.System;

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdateManufactureOrderHandler(
            _repositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProvider,
            _loggerMock.Object);
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
        updatedOrder!.SemiProductPlannedDate.Should().Be(request.SemiProductPlannedDate);
        updatedOrder.ProductPlannedDate.Should().Be(request.ProductPlannedDate);
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
        note.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
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

    private static UpdateManufactureOrderRequest CreateValidRequest()
    {
        return new UpdateManufactureOrderRequest
        {
            Id = ValidOrderId,
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(20)),
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
            SemiProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
            ProductPlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(15)),
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow.AddDays(-1),
            StateChangedByUser = "Original User"
        };

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