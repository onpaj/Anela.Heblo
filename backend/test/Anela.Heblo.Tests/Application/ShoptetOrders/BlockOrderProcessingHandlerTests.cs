using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class BlockOrderProcessingHandlerTests
{
    private readonly Mock<IEshopOrderClient> _clientMock;
    private readonly Mock<ILogger<BlockOrderProcessingHandler>> _loggerMock;
    private readonly ShoptetOrdersSettings _settings;
    private BlockOrderProcessingHandler CreateHandler() =>
        new BlockOrderProcessingHandler(
            _clientMock.Object,
            Options.Create(_settings),
            _loggerMock.Object);

    public BlockOrderProcessingHandlerTests()
    {
        _clientMock = new Mock<IEshopOrderClient>();
        _loggerMock = new Mock<ILogger<BlockOrderProcessingHandler>>();
        _settings = new ShoptetOrdersSettings
        {
            AllowedBlockSourceStateIds = [26, -2],
            BlockedStatusId = 99,
        };
    }

    [Fact]
    public async Task Handle_OrderInAllowedState_ChangesStatusAndAppendsEshopRemark()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("ORDER-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);

        var handler = new BlockOrderProcessingHandler(
            _clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        var response = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-A", Note = "test note" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _clientMock.Verify(c => c.UpdateStatusAsync("ORDER-A", 99, It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(
            c => c.AppendEshopRemarkAsync("ORDER-A", "test note", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OrderInSecondAllowedState_Succeeds()
    {
        _clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);

        var handler = new BlockOrderProcessingHandler(
            _clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        var response = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-B", Note = "note" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        _clientMock.Verify(c => c.UpdateStatusAsync("ORDER-B", 99, It.IsAny<CancellationToken>()), Times.Once);
        _clientMock.Verify(
            c => c.AppendEshopRemarkAsync("ORDER-B", "note", It.IsAny<CancellationToken>()),
            Times.Once);
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
        _clientMock.Verify(
            c => c.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        _clientMock.Verify(
            c => c.AppendEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ShoptetApiThrowsOnAppendEshopRemark_ReturnsSuccessAndLogsWarning()
    {
        var exception = new HttpRequestException("Shoptet unavailable");
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("ORDER-X", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.UpdateStatusAsync("ORDER-X", 99, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientMock.Setup(x => x.AppendEshopRemarkAsync("ORDER-X", "blocked", It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var result = await CreateHandler().Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-X", Note = "blocked" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _loggerMock.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("ORDER-X")),
            It.Is<Exception>(e => e != null),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CancellationOnRemarkStep_PropagatesOperationCanceledException()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("ORDER-X", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.UpdateStatusAsync("ORDER-X", 99, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientMock.Setup(x => x.AppendEshopRemarkAsync("ORDER-X", "blocked", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateHandler().Handle(
                new BlockOrderProcessingRequest { OrderCode = "ORDER-X", Note = "blocked" },
                CancellationToken.None));
    }
}
