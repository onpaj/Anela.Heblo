using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseOrderHistory;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase;

public sealed class GetPurchaseOrderHistoryHandlerTests
{
    private readonly Mock<ILogger<GetPurchaseOrderHistoryHandler>> _loggerMock = new();
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock = new();
    private readonly GetPurchaseOrderHistoryHandler _handler;

    public GetPurchaseOrderHistoryHandlerTests()
    {
        _handler = new GetPurchaseOrderHistoryHandler(_loggerMock.Object, _repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPurchaseOrderNotFound_WhenOrderDoesNotExist()
    {
        // Arrange
        const int missingId = 42;
        _repositoryMock
            .Setup(r => r.ExistsAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(missingId), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PurchaseOrderNotFound);
        response.Params.Should().ContainKey("Id").WhoseValue.Should().Be(missingId.ToString());

        _repositoryMock.Verify(r => r.GetHistoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyList_WhenOrderHasNoHistory()
    {
        // Arrange
        const int orderId = 7;
        _repositoryMock.Setup(r => r.ExistsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repositoryMock.Setup(r => r.GetHistoryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrderHistory>());

        // Act
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ReturnsMappedHistory_InRepositoryOrder()
    {
        // Arrange
        const int orderId = 11;
        var newer = new PurchaseOrderHistory(orderId, "StatusChanged", "Draft", "InTransit", "user-2");
        var older = new PurchaseOrderHistory(orderId, "Created", null, "Draft", "user-1");
        // Repository pre-orders newest-first; handler must preserve that order.
        var repoOutput = new List<PurchaseOrderHistory> { newer, older };

        _repositoryMock.Setup(r => r.ExistsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repositoryMock.Setup(r => r.GetHistoryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoOutput);

        // Act
        var response = await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(2);
        response.Items[0].Action.Should().Be("StatusChanged");
        response.Items[0].OldValue.Should().Be("Draft");
        response.Items[0].NewValue.Should().Be("InTransit");
        response.Items[0].ChangedBy.Should().Be("user-2");
        response.Items[1].Action.Should().Be("Created");
        response.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_DoesNotCallGetByIdWithDetailsAsync()
    {
        // Arrange — the whole point of this refactor: the handler must never load the aggregate.
        const int orderId = 99;
        _repositoryMock.Setup(r => r.ExistsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repositoryMock.Setup(r => r.GetHistoryAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrderHistory>());

        // Act
        await _handler.Handle(new GetPurchaseOrderHistoryRequest(orderId), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetByIdWithDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the history handler must never load lines / supplier / catalog data");
    }
}
