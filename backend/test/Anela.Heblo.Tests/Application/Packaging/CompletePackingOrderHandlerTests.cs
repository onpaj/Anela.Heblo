using Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Application.Packaging;

public class CompletePackingOrderHandlerTests
{
    private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();

    private CompletePackingOrderHandler CreateHandler() =>
        new(_eshopOrderClient.Object, new Mock<ILogger<CompletePackingOrderHandler>>().Object);

    [Fact]
    public async Task Handle_MarksOrderAsPacked_AndReturnsCompleted()
    {
        var response = await CreateHandler().Handle(
            new CompletePackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Completed.Should().BeTrue();
        _eshopOrderClient.Verify(
            c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMarkAsPackedThrows_ReturnsPackingCompletionFailed()
    {
        _eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet down"));

        var response = await CreateHandler().Handle(
            new CompletePackingOrderRequest { OrderCode = "0001234" },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PackingCompletionFailed);
    }
}
