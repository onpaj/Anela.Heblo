using Anela.Heblo.Application.Features.Purchase;
using Anela.Heblo.Application.Features.Purchase.Model;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class UpdatePurchaseOrderStatusHandlerTests
{
    private readonly Mock<ILogger<UpdatePurchaseOrderStatusHandler>> _loggerMock;
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly UpdatePurchaseOrderStatusHandler _handler;

    private static readonly int ValidOrderId = 999;
    private const string ValidOrderNumber = "PO-2024-001";

    public UpdatePurchaseOrderStatusHandlerTests()
    {
        _loggerMock = new Mock<ILogger<UpdatePurchaseOrderStatusHandler>>();
        _repositoryMock = new Mock<IPurchaseOrderRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdatePurchaseOrderStatusHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidRequestAndDraftOrder_ShouldUpdateStatusToInTransit()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(purchaseOrder.Id);
        result.OrderNumber.Should().Be(ValidOrderNumber);
        result.Status.Should().Be("InTransit");
        result.UpdatedBy.Should().Be("Test User");
        result.UpdatedAt.Should().NotBeNull();

        purchaseOrder.Status.Should().Be(PurchaseOrderStatus.InTransit);
    }

    [Fact]
    public async Task Handle_WithValidRequestAndInTransitOrder_ShouldUpdateStatusToCompleted()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");
        var purchaseOrder = CreateDraftPurchaseOrder();
        purchaseOrder.ChangeStatus(PurchaseOrderStatus.InTransit, "System");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Status.Should().Be("Completed");
        purchaseOrder.Status.Should().Be(PurchaseOrderStatus.Completed);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnNull()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithInvalidStatus_ShouldThrowArgumentException()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InvalidStatus");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid status: InvalidStatus");
    }

    [Fact]
    public async Task Handle_WithInvalidStatusTransition_ShouldThrowInvalidOperationException()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Invalid status transition from Draft to Completed*");
    }

    [Fact]
    public async Task Handle_ShouldCallRepositoryMethods()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldLogInformationMessages()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Updating purchase order status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("status updated to")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ShouldLogWarning()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InTransit");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder?)null);

        await _handler.Handle(request, CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Purchase order not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidStatus_ShouldLogWarning()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "InvalidStatus");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        var action = async () => await _handler.Handle(request, CancellationToken.None);
        await action.Should().ThrowAsync<ArgumentException>();

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Invalid status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidTransition_ShouldLogWarningAndRethrow()
    {
        var request = new UpdatePurchaseOrderStatusRequest(ValidOrderId, "Completed");
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        var action = async () => await _handler.Handle(request, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>();

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cannot update status")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static PurchaseOrder CreateDraftPurchaseOrder()
    {
        return new PurchaseOrder(
            ValidOrderNumber,
            "Test Supplier",
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            "Test notes",
            "System");
    }
}