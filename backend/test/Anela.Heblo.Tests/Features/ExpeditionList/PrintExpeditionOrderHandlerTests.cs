using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ExpeditionList.UseCases.PrintExpeditionOrder;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class PrintExpeditionOrderHandlerTests
{
    private readonly Mock<IExpeditionListService> _service = new();
    private readonly Mock<IEshopOrderClient> _client = new();

    private PrintExpeditionOrderHandler CreateHandler() => new(
        _service.Object,
        _client.Object,
        Options.Create(new PrintPickingListOptions()),
        new Mock<ILogger<PrintExpeditionOrderHandler>>().Object);

    [Theory]
    [InlineData(-3)]
    [InlineData(26)]
    [InlineData(52)]
    [InlineData(70)]
    public async Task Handle_OrderInNonPrintableState_ReturnsInvalidStateError(int statusId)
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(statusId);

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ExpeditionOrderInvalidState);
        _service.Verify(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidState_PrintsWithOrderCodeAndDesiredState26()
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        ExpeditionPickingRequest? captured = null;
        _service.Setup(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .Callback<ExpeditionPickingRequest, IList<string>?, CancellationToken>((r, _, _) => captured = r)
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 1 });

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        captured!.OrderCode.Should().Be("0001234");
        captured.DesiredStateId.Should().Be(26);
        captured.ChangeOrderState.Should().BeTrue();
        captured.SendToPrinter.Should().BeTrue();
        captured.Carriers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NothingPrinted_ReturnsNotPrintedError()
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("0001234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(-2);
        _service.Setup(s => s.PrintPickingListAsync(It.IsAny<ExpeditionPickingRequest>(), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExpeditionPickingResult { ExportedFiles = new List<string>(), TotalCount = 0 });

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "0001234" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ExpeditionOrderNotPrinted);
    }

    [Fact]
    public async Task Handle_OrderLookupThrows_ReturnsNotFoundError()
    {
        _client.Setup(c => c.GetOrderStatusIdAsync("nope", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404"));

        var result = await CreateHandler().Handle(
            new PrintExpeditionOrderRequest { OrderCode = "nope" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ShoptetOrderNotFound);
    }
}
