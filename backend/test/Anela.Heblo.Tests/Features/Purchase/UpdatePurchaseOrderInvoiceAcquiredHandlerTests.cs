using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrderInvoiceAcquired;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public class UpdatePurchaseOrderInvoiceAcquiredHandlerTests
{
    private readonly Mock<ILogger<UpdatePurchaseOrderInvoiceAcquiredHandler>> _loggerMock;
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly UpdatePurchaseOrderInvoiceAcquiredHandler _handler;

    private static readonly int ValidOrderId = 999;
    private const string ValidOrderNumber = "PO-2024-001";

    public UpdatePurchaseOrderInvoiceAcquiredHandlerTests()
    {
        _loggerMock = new Mock<ILogger<UpdatePurchaseOrderInvoiceAcquiredHandler>>();
        _repositoryMock = new Mock<IPurchaseOrderRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user-id", "Test User", "test@example.com", true));

        _handler = new UpdatePurchaseOrderInvoiceAcquiredHandler(
            _loggerMock.Object,
            _repositoryMock.Object,
            _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnError()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseOrder?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderNotFound);
        result.Params.Should().ContainKey("Id");
        result.Params["Id"].Should().Be(ValidOrderId.ToString());

        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldSetInvoiceAcquiredAndPersist()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };
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
        result.Success.Should().BeTrue();
        result.Id.Should().Be(purchaseOrder.Id);
        result.InvoiceAcquired.Should().BeTrue();

        purchaseOrder.InvoiceAcquired.Should().BeTrue();

        _repositoryMock.Verify(x => x.UpdateAsync(purchaseOrder, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUpdateAsyncThrows_ShouldReturnUpdateFailedError()
    {
        var request = new UpdatePurchaseOrderInvoiceAcquiredRequest { Id = ValidOrderId, InvoiceAcquired = true };
        var purchaseOrder = CreateDraftPurchaseOrder();

        _repositoryMock
            .Setup(x => x.GetByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(purchaseOrder);

        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<PurchaseOrder>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db unavailable"));

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderUpdateFailed);
        result.Params.Should().ContainKey("OrderNumber");
        result.Params["OrderNumber"].Should().Be(ValidOrderNumber);
        result.Params.Should().ContainKey("Message");
        result.Params["Message"].Should().Be("db unavailable");
    }

    private static PurchaseOrder CreateDraftPurchaseOrder()
    {
        return new PurchaseOrder(
            ValidOrderNumber,
            1, // SupplierId
            "Test Supplier",
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(14),
            null, // ContactVia
            "Test notes",
            "System");
    }
}
