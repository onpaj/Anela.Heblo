using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Logistics;

public class TransportBoxItemGroupingTests
{
    private static readonly DateTime TestDate = new(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
    private const string TestUser = "Test User";

    private static TransportBox CreateOpenBox()
    {
        var box = new TransportBox();
        box.Open("B001", TestDate, TestUser);
        return box;
    }

    [Fact]
    public void AddItem_SameProductLotExpirationSource_MergesIntoSingleRowWithSummedAmount()
    {
        // Arrange
        var box = CreateOpenBox();
        var expiration = new DateOnly(2026, 1, 1);

        // Act
        box.AddItem("PROD-001", "Product", 2.0, TestDate, TestUser, lotNumber: "L1", expirationDate: expiration, sourceInventoryId: 10);
        box.AddItem("PROD-001", "Product", 3.0, TestDate, TestUser, lotNumber: "L1", expirationDate: expiration, sourceInventoryId: 10);

        // Assert
        box.Items.Should().ContainSingle();
        box.Items[0].Amount.Should().Be(5.0);
    }

    [Fact]
    public void AddItem_DifferentLot_CreatesSeparateRows()
    {
        // Arrange
        var box = CreateOpenBox();

        // Act
        box.AddItem("PROD-001", "Product", 2.0, TestDate, TestUser, lotNumber: "L1", sourceInventoryId: 10);
        box.AddItem("PROD-001", "Product", 3.0, TestDate, TestUser, lotNumber: "L2", sourceInventoryId: 11);

        // Assert
        box.Items.Should().HaveCount(2);
    }

    [Fact]
    public void AddItem_DifferentProductCode_CreatesSeparateRows()
    {
        // Arrange
        var box = CreateOpenBox();

        // Act
        box.AddItem("PROD-001", "Product 1", 2.0, TestDate, TestUser);
        box.AddItem("PROD-002", "Product 2", 3.0, TestDate, TestUser);

        // Assert
        box.Items.Should().HaveCount(2);
    }

    [Fact]
    public void AddItem_CatalogItemsWithoutLot_MergeByProductCode()
    {
        // Arrange — catalog items carry no lot/expiration/source
        var box = CreateOpenBox();

        // Act
        box.AddItem("GOODS-001", "Goods", 1.0, TestDate, TestUser);
        box.AddItem("GOODS-001", "Goods", 4.0, TestDate, TestUser);

        // Assert
        box.Items.Should().ContainSingle();
        box.Items[0].Amount.Should().Be(5.0);
    }

    [Fact]
    public void DecreaseItem_PartialAmount_KeepsRowAndReturnsRemovedAmount()
    {
        // Arrange
        var box = CreateOpenBox();
        box.AddItem("PROD-001", "Product", 10.0, TestDate, TestUser, sourceInventoryId: 10);
        var itemId = box.Items[0].Id;

        // Act
        var result = box.DecreaseItem(itemId, 4.0);

        // Assert
        result.Should().NotBeNull();
        result!.RemovedAmount.Should().Be(4.0);
        result.RowRemoved.Should().BeFalse();
        box.Items.Should().ContainSingle();
        box.Items[0].Amount.Should().Be(6.0);
    }

    [Fact]
    public void DecreaseItem_FullAmount_RemovesRow()
    {
        // Arrange
        var box = CreateOpenBox();
        box.AddItem("PROD-001", "Product", 5.0, TestDate, TestUser, sourceInventoryId: 10);
        var itemId = box.Items[0].Id;

        // Act
        var result = box.DecreaseItem(itemId, 5.0);

        // Assert
        result.Should().NotBeNull();
        result!.RemovedAmount.Should().Be(5.0);
        result.RowRemoved.Should().BeTrue();
        box.Items.Should().BeEmpty();
    }

    [Fact]
    public void DecreaseItem_AmountExceedingRemaining_ClampsRemovedAndRemovesRow()
    {
        // Arrange
        var box = CreateOpenBox();
        box.AddItem("PROD-001", "Product", 5.0, TestDate, TestUser, sourceInventoryId: 10);
        var itemId = box.Items[0].Id;

        // Act
        var result = box.DecreaseItem(itemId, 8.0);

        // Assert
        result.Should().NotBeNull();
        result!.RemovedAmount.Should().Be(5.0);
        result.RowRemoved.Should().BeTrue();
        box.Items.Should().BeEmpty();
    }

    [Fact]
    public void DecreaseItem_UnknownItemId_ReturnsNull()
    {
        // Arrange
        var box = CreateOpenBox();
        box.AddItem("PROD-001", "Product", 5.0, TestDate, TestUser);

        // Act
        var result = box.DecreaseItem(999, 1.0);

        // Assert
        result.Should().BeNull();
    }
}
