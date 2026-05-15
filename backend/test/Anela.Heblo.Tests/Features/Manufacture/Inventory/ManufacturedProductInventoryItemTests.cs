using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Manufacture.Inventory;

public class ManufacturedProductInventoryItemTests
{
    private static ManufacturedProductInventoryItem CreateItem(decimal amount = 10m) =>
        new("PROD-001", "Test Product", amount, "user1",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            lotNumber: "LOT-001",
            expirationDate: new DateOnly(2027, 6, 1),
            manufactureOrderId: 42);

    [Fact]
    public void Constructor_SetsFieldsAndCreatesInitialLog()
    {
        var item = CreateItem(10m);

        item.ProductCode.Should().Be("PROD-001");
        item.Amount.Should().Be(10m);
        item.LotNumber.Should().Be("LOT-001");
        item.ManufactureOrderId.Should().Be(42);
        item.Log.Should().HaveCount(1);
        item.Log[0].ChangeType.Should().Be(InventoryChangeType.InitialWriteDown);
        item.Log[0].AmountDelta.Should().Be(10m);
        item.Log[0].AmountAfter.Should().Be(10m);
    }

    [Fact]
    public void Consume_ReducesAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        item.Consume(3m, "packer1", ts, transportBoxId: 7);

        item.Amount.Should().Be(7m);
        item.Log.Should().HaveCount(2);
        var log = item.Log[1];
        log.ChangeType.Should().Be(InventoryChangeType.ConsumedByTransportBox);
        log.AmountDelta.Should().Be(-3m);
        log.AmountAfter.Should().Be(7m);
        log.ReferenceType.Should().Be("TransportBox");
        log.ReferenceId.Should().Be("7");
    }

    [Fact]
    public void Consume_WhenAmountExceedsStock_Throws()
    {
        var item = CreateItem(5m);

        var act = () => item.Consume(6m, "packer1",
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Insufficient*");
    }

    [Fact]
    public void Restore_IncreasesAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        item.Consume(3m, "u", ts);

        item.Restore(3m, "u", ts, transportBoxId: 7);

        item.Amount.Should().Be(10m);
        item.Log.Last().ChangeType.Should().Be(InventoryChangeType.RestoredFromTransportBox);
        item.Log.Last().AmountDelta.Should().Be(3m);
    }

    [Fact]
    public void ManualAdjust_SetsNewAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        item.ManualAdjust(15m, "admin", ts, note: "recount");

        item.Amount.Should().Be(15m);
        item.Log.Last().ChangeType.Should().Be(InventoryChangeType.ManualAdjustment);
        item.Log.Last().AmountDelta.Should().Be(5m);
        item.Log.Last().Note.Should().Be("recount");
    }

    [Fact]
    public void ManualRemove_ZeroesAmountAndAddsLog()
    {
        var item = CreateItem(10m);
        var ts = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        item.ManualRemove("admin", ts, note: "expired");

        item.Amount.Should().Be(0m);
        item.Log.Last().ChangeType.Should().Be(InventoryChangeType.ManualRemoval);
        item.Log.Last().AmountDelta.Should().Be(-10m);
    }
}
