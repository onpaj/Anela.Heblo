using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class MapOrderItemsTests
{
    [Fact]
    public void MapOrderItems_WithProductSet_SetsSetNameOnComponents()
    {
        var detail = new ExpeditionOrderDetail
        {
            Code = "0001",
            Items =
            [
                new ExpeditionOrderItemDto
                {
                    ItemType = "product-set",
                    ItemId = 10,
                    Name = "Starter Kit",
                    Amount = 1,
                },
            ],
            Completion =
            [
                new ExpeditionCompletionItemDto
                {
                    ItemType = "product-set-item",
                    ItemId = 101,
                    ParentProductSetItemId = 10,
                    Code = "SKU-A",
                    Name = "Component A",
                    Amount = 2,
                },
                new ExpeditionCompletionItemDto
                {
                    ItemType = "product-set-item",
                    ItemId = 102,
                    ParentProductSetItemId = 10,
                    Code = "SKU-B",
                    Name = "Component B",
                    Amount = 1,
                },
            ],
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(2);
        items.Should().AllSatisfy(i => i.SetName.Should().Be("Starter Kit"));
        items.Should().AllSatisfy(i => i.IsFromSet.Should().BeTrue());
    }

    [Fact]
    public void MapOrderItems_WithRegularProduct_HasNullSetName()
    {
        var detail = new ExpeditionOrderDetail
        {
            Code = "0002",
            Items =
            [
                new ExpeditionOrderItemDto
                {
                    ItemType = "product",
                    ItemId = 20,
                    Code = "SKU-X",
                    Name = "Regular Product",
                    Amount = 3,
                },
            ],
            Completion = [],
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(1);
        items[0].SetName.Should().BeNull();
        items[0].IsFromSet.Should().BeFalse();
    }

    [Fact]
    public void MapOrderItems_WithMultipleSets_SetsCorrectSetNamePerGroup()
    {
        var detail = new ExpeditionOrderDetail
        {
            Code = "0003",
            Items =
            [
                new ExpeditionOrderItemDto { ItemType = "product-set", ItemId = 10, Name = "Kit Alpha", Amount = 1 },
                new ExpeditionOrderItemDto { ItemType = "product-set", ItemId = 20, Name = "Kit Beta", Amount = 2 },
            ],
            Completion =
            [
                new ExpeditionCompletionItemDto { ItemType = "product-set-item", ItemId = 101, ParentProductSetItemId = 10, Code = "A1", Name = "Alpha Part", Amount = 1 },
                new ExpeditionCompletionItemDto { ItemType = "product-set-item", ItemId = 201, ParentProductSetItemId = 20, Code = "B1", Name = "Beta Part", Amount = 3 },
            ],
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(2);
        items.Single(i => i.ProductCode == "A1").SetName.Should().Be("Kit Alpha");
        items.Single(i => i.ProductCode == "B1").SetName.Should().Be("Kit Beta");
        // Kit Beta has quantity 2 (set) * 3 (component) = 6
        items.Single(i => i.ProductCode == "B1").Quantity.Should().Be(6);
    }
}
