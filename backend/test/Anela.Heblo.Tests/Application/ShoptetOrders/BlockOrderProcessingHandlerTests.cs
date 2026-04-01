using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class BlockOrderProcessingHandlerTests
{
    private readonly Mock<IShoptetOrderClient> _clientMock;
    private readonly Mock<ILogger<BlockOrderProcessingHandler>> _loggerMock;
    private readonly ShoptetOrdersSettings _settings;
    private BlockOrderProcessingHandler CreateHandler() =>
        new BlockOrderProcessingHandler(
            _clientMock.Object,
            Options.Create(_settings),
            _loggerMock.Object);

    public BlockOrderProcessingHandlerTests()
    {
        _clientMock = new Mock<IShoptetOrderClient>();
        _loggerMock = new Mock<ILogger<BlockOrderProcessingHandler>>();
        _settings = new ShoptetOrdersSettings
        {
            AllowedBlockSourceStateIds = [26, -2],
            BlockedStatusId = 99,
        };
    }

    [Fact]
    public async Task Handle_OrderInAllowedState_ChangesStatusAndSetsNote()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.UpdateStatusAsync("0001234", 99, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientMock.Setup(x => x.SetInternalNoteAsync("0001234", "Blocked for review", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "Blocked for review" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _clientMock.Verify(x => x.UpdateStatusAsync("0001234", 99, It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(x => x.SetInternalNoteAsync("0001234", "Blocked for review", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OrderInSecondAllowedState_Succeeds()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0005678", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        _clientMock.Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientMock.Setup(x => x.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0005678", Note = "note" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OrderInDisallowedState_ReturnsInvalidSourceStateError_WithoutCallingShoptet()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(70);

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "note" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderInvalidSourceState);
        result.Params.Should().ContainKey("orderCode").WhoseValue.Should().Be("0001234");
        result.Params.Should().ContainKey("currentStatusId").WhoseValue.Should().Be("70");
        _clientMock.Verify(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _clientMock.Verify(x => x.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShoptetApiThrowsOnStatusFetch_ReturnsInternalServerError()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet unavailable"));

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "note" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_ShoptetApiThrowsOnStatusUpdate_ReturnsInternalServerError()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.UpdateStatusAsync("0001234", 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Status update failed"));

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "0001234", Note = "note" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        _clientMock.Verify(x => x.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
