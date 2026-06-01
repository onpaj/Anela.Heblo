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
    public async Task Handle_OrderInAllowedState_ChangesStatusAndUpdatesEshopRemark()
    {
        _clientMock.Setup(x => x.GetOrderStatusIdAsync("ORDER-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        _clientMock.Setup(x => x.GetEshopRemarkAsync("ORDER-A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

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
            c => c.UpdateEshopRemarkAsync("ORDER-A", "test note", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OrderInSecondAllowedState_Succeeds()
    {
        _clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        _clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-B", It.IsAny<CancellationToken>()))
            .ReturnsAsync("existing");

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
            c => c.UpdateEshopRemarkAsync("ORDER-B", "existing\nnote", It.IsAny<CancellationToken>()),
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
            c => c.GetEshopRemarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clientMock.Verify(
            c => c.UpdateEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
            c => c.GetEshopRemarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clientMock.Verify(
            c => c.UpdateEshopRemarkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OrderInAllowedState_AppendsNoteToExistingEshopRemark()
    {
        // Arrange
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(26);
        clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("previous staff note");

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        // Act
        var result = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-1", Note = "blocked by accounting" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        clientMock.Verify(c => c.UpdateStatusAsync("ORDER-1", 99, It.IsAny<CancellationToken>()), Times.Once);
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync(
                "ORDER-1",
                "previous staff note\nblocked by accounting",
                It.IsAny<CancellationToken>()),
            Times.Once);
        clientMock.Verify(
            c => c.SetInternalNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_OrderWithEmptyEshopRemark_SetsNoteAsFirstLine()
    {
        // Arrange
        var clientMock = new Mock<IEshopOrderClient>();
        clientMock.Setup(c => c.GetOrderStatusIdAsync("ORDER-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        clientMock.Setup(c => c.GetEshopRemarkAsync("ORDER-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var handler = new BlockOrderProcessingHandler(
            clientMock.Object,
            Options.Create(new ShoptetOrdersSettings
            {
                AllowedBlockSourceStateIds = new[] { 26, -2 },
                BlockedStatusId = 99,
            }),
            NullLogger<BlockOrderProcessingHandler>.Instance);

        // Act
        var result = await handler.Handle(
            new BlockOrderProcessingRequest { OrderCode = "ORDER-2", Note = "fraud suspicion" },
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        clientMock.Verify(
            c => c.UpdateEshopRemarkAsync("ORDER-2", "fraud suspicion", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
