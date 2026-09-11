using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class ReceivedSideEffectTests
{
    private readonly Mock<ILogisticsStockOperationService> _stockOperationServiceMock = new();
    private readonly ReceivedSideEffect _sut;

    public ReceivedSideEffectTests()
    {
        _stockOperationServiceMock
            .Setup(x => x.StageOperationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new ReceivedSideEffect(_stockOperationServiceMock.Object, NullLogger<ReceivedSideEffect>.Instance);
    }

    [Theory]
    [InlineData(TransportBoxState.InTransit)]
    [InlineData(TransportBoxState.Reserve)]
    [InlineData(TransportBoxState.Quarantine)]
    public void Supports_KnownOriginsToReceived_ReturnsTrue(TransportBoxState from)
    {
        _sut.Supports(from, TransportBoxState.Received).Should().BeTrue();
    }

    [Fact]
    public void Supports_NewToReceived_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.New, TransportBoxState.Received).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesItemsByProductCode_StagesOneOperationPerProduct()
    {
        var box = CreateBoxWithItems(("SKU-1", 2.0), ("SKU-1", 3.0), ("SKU-2", 1.0));
        var request = new ChangeTransportBoxStateRequest { BoxId = box.Id, NewState = TransportBoxState.Received };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            $"BOX-{box.Id:000000}-SKU-1", "SKU-1", 5,
            LogisticsStockOperationSource.TransportBox, box.Id, It.IsAny<CancellationToken>()), Times.Once);
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            $"BOX-{box.Id:000000}-SKU-2", "SKU-2", 1,
            LogisticsStockOperationSource.TransportBox, box.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RoundsFractionalAmounts_AwayFromZero()
    {
        var box = CreateBoxWithItems(("SKU-1", 1.4), ("SKU-1", 1.4));
        var request = new ChangeTransportBoxStateRequest { BoxId = box.Id, NewState = TransportBoxState.Received };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
        _stockOperationServiceMock.Verify(x => x.StageOperationAsync(
            It.IsAny<string>(), "SKU-1", 3,
            It.IsAny<LogisticsStockOperationSource>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TransportBox CreateBoxWithItems(params (string ProductCode, double Amount)[] items)
    {
        var box = new TransportBox { Id = 1 };

        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (List<TransportBoxItem>)itemsField!.GetValue(box)!;

        foreach (var (productCode, amount) in items)
        {
            list.Add(new TransportBoxItem(productCode, "Product", amount, DateTime.UtcNow, "TestUser"));
        }

        return box;
    }
}
