using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShoptetOrders.Infrastructure;

public class ShoptetOrdersPackedOrderStatusUpdaterAdapterTests
{
    [Fact]
    public async Task MarkAsPackedAsync_DelegatesToEshopOrderClient_WithSameArguments()
    {
        var orderCode = "0001234";
        using var cts = new CancellationTokenSource();
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync(orderCode, cts.Token))
            .Returns(Task.CompletedTask);
        var sut = new ShoptetOrdersPackedOrderStatusUpdaterAdapter(eshopOrderClient.Object);

        await sut.MarkAsPackedAsync(orderCode, cts.Token);

        eshopOrderClient.Verify(c => c.MarkAsPackedAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task MarkAsPackedAsync_PropagatesException_WhenEshopOrderClientThrows()
    {
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync("0005678", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet down"));
        var sut = new ShoptetOrdersPackedOrderStatusUpdaterAdapter(eshopOrderClient.Object);

        var act = () => sut.MarkAsPackedAsync("0005678");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
