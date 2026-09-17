using System.Net;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShoptetOrders.Infrastructure;

public class ShoptetOrdersOrderStatusReaderAdapterTests
{
    [Fact]
    public async Task GetOrderStatusIdAsync_DelegatesToEshopOrderClient_WithSameArgumentsAndResult()
    {
        var orderCode = "0001234";
        using var cts = new CancellationTokenSource();
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.GetOrderStatusIdAsync(orderCode, cts.Token))
            .ReturnsAsync(26);
        var sut = new ShoptetOrdersOrderStatusReaderAdapter(eshopOrderClient.Object);

        var result = await sut.GetOrderStatusIdAsync(orderCode, cts.Token);

        result.Should().Be(26);
        eshopOrderClient.Verify(c => c.GetOrderStatusIdAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetOrderStatusIdAsync_PropagatesNotFoundHttpRequestException_Unmodified()
    {
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.GetOrderStatusIdAsync("nope", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.NotFound));
        var sut = new ShoptetOrdersOrderStatusReaderAdapter(eshopOrderClient.Object);

        var act = () => sut.GetOrderStatusIdAsync("nope");

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
