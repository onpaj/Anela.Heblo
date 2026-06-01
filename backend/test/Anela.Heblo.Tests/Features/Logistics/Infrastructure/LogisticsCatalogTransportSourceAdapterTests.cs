using Anela.Heblo.Application.Features.Logistics.Infrastructure;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Infrastructure;

public class LogisticsCatalogTransportSourceAdapterTests
{
    private readonly Mock<ITransportBoxRepository> _transportBoxRepository = new();

    private LogisticsCatalogTransportSourceAdapter CreateAdapter() =>
        new(_transportBoxRepository.Object);

    private static TransportBox MakeTransportBox(int id, TransportBoxState state, params (string productCode, double amount)[] items)
    {
        var box = new TransportBox { Id = id };

        // Set state using reflection since it's read-only
        var stateProperty = typeof(TransportBox).GetProperty("State");
        stateProperty?.SetValue(box, state);

        // Set items using the backing field
        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (itemsField != null)
        {
            var itemsList = (List<TransportBoxItem>)itemsField.GetValue(box)!;
            foreach (var (productCode, amount) in items)
            {
                itemsList.Add(new TransportBoxItem(
                    productCode: productCode,
                    productName: $"Product {productCode}",
                    amount: amount,
                    dateAdded: DateTime.UtcNow,
                    userAdded: "test-user"));
            }
        }

        return box;
    }

    [Fact]
    public async Task GetProductsInTransportAsync_ReturnsEmptyDictionary_WhenNoBoxesInTransport()
    {
        var ct = CancellationToken.None;
        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInTransportPredicate,
                true,
                ct))
            .ReturnsAsync(Array.Empty<TransportBox>());

        var result = await CreateAdapter().GetProductsInTransportAsync(ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductsInTransportAsync_ReturnsSingleProduct_WhenBoxHasSingleItem()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.InTransit, ("PROD-1", 10.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInTransportPredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInTransportAsync(ct);

        result.Should().HaveCount(1);
        result["PROD-1"].Should().Be(10);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_AggregatesTotalAmount_WhenMultipleBoxesHaveSameProduct()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.InTransit, ("PROD-1", 5.0)),
            MakeTransportBox(2, TransportBoxState.InTransit, ("PROD-1", 3.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInTransportPredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInTransportAsync(ct);

        result.Should().HaveCount(1);
        result["PROD-1"].Should().Be(8);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_HandleMultipleProducts_GoldenDataset()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.InTransit,
                ("PROD-1", 5.0), ("PROD-2", 10.0)),
            MakeTransportBox(2, TransportBoxState.Received,
                ("PROD-1", 3.0), ("PROD-3", 7.0)),
            MakeTransportBox(3, TransportBoxState.Opened,
                ("PROD-2", 2.0), ("PROD-3", 1.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInTransportPredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInTransportAsync(ct);

        result.Should().HaveCount(3);
        result["PROD-1"].Should().Be(8); // 5 + 3
        result["PROD-2"].Should().Be(12); // 10 + 2
        result["PROD-3"].Should().Be(8); // 7 + 1
    }

    [Fact]
    public async Task GetProductsInReserveAsync_ReturnsEmptyDictionary_WhenNoBoxesInReserve()
    {
        var ct = CancellationToken.None;
        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInReservePredicate,
                true,
                ct))
            .ReturnsAsync(Array.Empty<TransportBox>());

        var result = await CreateAdapter().GetProductsInReserveAsync(ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductsInReserveAsync_ReturnsSingleProduct_WhenBoxHasSingleItem()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.Reserve, ("PROD-1", 15.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInReservePredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInReserveAsync(ct);

        result.Should().HaveCount(1);
        result["PROD-1"].Should().Be(15);
    }

    [Fact]
    public async Task GetProductsInReserveAsync_AggregatesTotalAmount_WhenMultipleBoxesHaveSameProduct()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.Reserve, ("PROD-1", 6.0)),
            MakeTransportBox(2, TransportBoxState.Reserve, ("PROD-1", 4.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInReservePredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInReserveAsync(ct);

        result.Should().HaveCount(1);
        result["PROD-1"].Should().Be(10);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_ReturnsEmptyDictionary_WhenNoBoxesInQuarantine()
    {
        var ct = CancellationToken.None;
        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInQuarantinePredicate,
                true,
                ct))
            .ReturnsAsync(Array.Empty<TransportBox>());

        var result = await CreateAdapter().GetProductsInQuarantineAsync(ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_ReturnsSingleProduct_WhenBoxHasSingleItem()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.Quarantine, ("PROD-1", 8.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInQuarantinePredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInQuarantineAsync(ct);

        result.Should().HaveCount(1);
        result["PROD-1"].Should().Be(8);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_AggregatesTotalAmount_WhenMultipleBoxesHaveSameProduct()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.Quarantine, ("PROD-1", 2.0)),
            MakeTransportBox(2, TransportBoxState.Quarantine, ("PROD-1", 3.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInQuarantinePredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInQuarantineAsync(ct);

        result.Should().HaveCount(1);
        result["PROD-1"].Should().Be(5);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_HandleMultipleProducts_GoldenDataset()
    {
        var ct = CancellationToken.None;
        var boxes = new[]
        {
            MakeTransportBox(1, TransportBoxState.Quarantine,
                ("PROD-A", 2.5), ("PROD-B", 1.0)),
            MakeTransportBox(2, TransportBoxState.Quarantine,
                ("PROD-A", 1.5), ("PROD-C", 3.0))
        };

        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInQuarantinePredicate,
                true,
                ct))
            .ReturnsAsync(boxes);

        var result = await CreateAdapter().GetProductsInQuarantineAsync(ct);

        result.Should().HaveCount(3);
        result["PROD-A"].Should().Be(4); // 2.5 + 1.5
        result["PROD-B"].Should().Be(1);
        result["PROD-C"].Should().Be(3);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_PassesCancellationTokenThroughToRepository()
    {
        var ct = new CancellationToken(false);
        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInTransportPredicate,
                true,
                ct))
            .ReturnsAsync(Array.Empty<TransportBox>());

        await CreateAdapter().GetProductsInTransportAsync(ct);

        _transportBoxRepository.Verify(
            r => r.FindAsync(
                TransportBox.IsInTransportPredicate,
                true,
                ct),
            Times.Once);
    }

    [Fact]
    public async Task GetProductsInReserveAsync_PassesCancellationTokenThroughToRepository()
    {
        var ct = new CancellationToken(false);
        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInReservePredicate,
                true,
                ct))
            .ReturnsAsync(Array.Empty<TransportBox>());

        await CreateAdapter().GetProductsInReserveAsync(ct);

        _transportBoxRepository.Verify(
            r => r.FindAsync(
                TransportBox.IsInReservePredicate,
                true,
                ct),
            Times.Once);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_PassesCancellationTokenThroughToRepository()
    {
        var ct = new CancellationToken(false);
        _transportBoxRepository
            .Setup(r => r.FindAsync(
                TransportBox.IsInQuarantinePredicate,
                true,
                ct))
            .ReturnsAsync(Array.Empty<TransportBox>());

        await CreateAdapter().GetProductsInQuarantineAsync(ct);

        _transportBoxRepository.Verify(
            r => r.FindAsync(
                TransportBox.IsInQuarantinePredicate,
                true,
                ct),
            Times.Once);
    }
}
