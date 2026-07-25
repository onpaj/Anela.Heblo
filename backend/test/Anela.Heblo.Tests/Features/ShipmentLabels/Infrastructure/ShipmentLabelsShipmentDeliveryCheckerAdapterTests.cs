using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShipmentLabels.Infrastructure;

public class ShipmentLabelsShipmentDeliveryCheckerAdapterTests
{
    [Fact]
    public async Task HasDeliveredShipmentAsync_DelegatesToShipmentClient_WithSameArgumentsAndResult()
    {
        var orderCode = "ORD-123";
        using var cts = new CancellationTokenSource();
        var shipmentClient = new Mock<IShipmentClient>();
        shipmentClient
            .Setup(c => c.HasDeliveredShipmentAsync(orderCode, cts.Token))
            .ReturnsAsync(true);
        var sut = new ShipmentLabelsShipmentDeliveryCheckerAdapter(shipmentClient.Object);

        var result = await sut.HasDeliveredShipmentAsync(orderCode, cts.Token);

        result.Should().BeTrue();
        shipmentClient.Verify(c => c.HasDeliveredShipmentAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task HasDeliveredShipmentAsync_ReturnsFalse_WhenShipmentClientReturnsFalse()
    {
        var shipmentClient = new Mock<IShipmentClient>();
        shipmentClient
            .Setup(c => c.HasDeliveredShipmentAsync("ORD-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = new ShipmentLabelsShipmentDeliveryCheckerAdapter(shipmentClient.Object);

        var result = await sut.HasDeliveredShipmentAsync("ORD-456");

        result.Should().BeFalse();
    }
}
