using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Application.ShoptetOrders;

public class DoubleShotSwapPlannerTests
{
    private static readonly int[] Eligible = [-1, 3, 32, -2, 35, 73];

    private static EshopOrderLine Line(long id, string type, string? code, decimal amount = 1m,
        decimal price = 100m, decimal ratio = 1m, string? remark = null) =>
        new()
        {
            ItemId = id,
            ItemType = type,
            Code = code,
            Amount = amount,
            UnitPriceWithVat = price,
            VatRate = 21m,
            PriceRatio = ratio,
            Remark = remark,
        };

    private static EshopOrderDetail Order(int status, params EshopOrderLine[] lines) =>
        new() { Code = "126000043", StatusId = status, Lines = lines };

    [Fact]
    public void TagFor_FormatsVariantCodeAndItemId()
    {
        DoubleShotSwapPlanner.TagFor("SA015030V", 1356551).Should().Be("auto-swap SA015030V #1356551");
    }

    [Fact]
    public void Plan_SwapsOnlyVariantLines_InMixedOrder()
    {
        var order = Order(-2,
            Line(1, "product", "SA015030V", amount: 2m, price: 1770m, ratio: 0.9m),
            Line(2, "product", "SER002030"),
            Line(3, "product-set", "SA014030S"),
            Line(4, "shipping", null),
            Line(5, "billing", null));

        var plan = DoubleShotSwapPlanner.Plan(order, Eligible);

        plan.IsEligible.Should().BeTrue();
        plan.OrderCode.Should().Be("126000043");
        var action = plan.Actions.Should().ContainSingle().Subject;
        action.VariantItemId.Should().Be(1);
        action.VariantCode.Should().Be("SA015030V");
        action.SetCode.Should().Be("SA015030S");
        action.Amount.Should().Be(2m);
        action.IsHeal.Should().BeFalse();
        action.SetItemToAdd.Should().BeEquivalentTo(new NewEshopOrderItem
        {
            ItemType = "product-set",
            Code = "SA015030S",
            Amount = 2m,
            UnitPriceWithVat = 1770m,
            VatRate = 21m,
            PriceRatio = 0.9m,
            Remark = "auto-swap SA015030V #1",
        });
    }

    [Fact]
    public void Plan_KeepsTwoLinesOfSameVariantSeparate()
    {
        var order = Order(-1,
            Line(10, "product", "SA015030V", amount: 1m, ratio: 1m),
            Line(11, "product", "SA015030V", amount: 3m, ratio: 0.5m));

        var plan = DoubleShotSwapPlanner.Plan(order, Eligible);

        plan.Actions.Should().HaveCount(2);
        plan.Actions[0].SetItemToAdd!.Remark.Should().Be("auto-swap SA015030V #10");
        plan.Actions[0].SetItemToAdd!.PriceRatio.Should().Be(1m);
        plan.Actions[1].SetItemToAdd!.Remark.Should().Be("auto-swap SA015030V #11");
        plan.Actions[1].SetItemToAdd!.Amount.Should().Be(3m);
        plan.Actions[1].SetItemToAdd!.PriceRatio.Should().Be(0.5m);
    }

    [Fact]
    public void Plan_HealsHalfSwap_WhenTaggedSetLineExists()
    {
        var order = Order(-2,
            Line(1, "product", "SA015030V", amount: 2m),
            Line(7, "product-set", "SA015030S", amount: 2m, remark: "auto-swap SA015030V #1"));

        var action = DoubleShotSwapPlanner.Plan(order, Eligible).Actions.Should().ContainSingle().Subject;

        action.IsHeal.Should().BeTrue();
        action.SetItemToAdd.Should().BeNull();
        action.VariantItemId.Should().Be(1);
    }

    [Fact]
    public void Plan_DoesNotHeal_WhenSetLineTagBelongsToAnotherVariantLine()
    {
        var order = Order(-2,
            Line(2, "product", "SA015030V"),
            Line(7, "product-set", "SA015030S", remark: "auto-swap SA015030V #1"));

        DoubleShotSwapPlanner.Plan(order, Eligible).Actions.Single().IsHeal.Should().BeFalse();
    }

    [Fact]
    public void Plan_IsNotEligible_WhenStatusNotInList()
    {
        var plan = DoubleShotSwapPlanner.Plan(Order(26, Line(1, "product", "SA015030V")), Eligible);

        plan.IsEligible.Should().BeFalse();
        plan.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Plan_IsEmpty_WhenNoVariantLines()
    {
        var plan = DoubleShotSwapPlanner.Plan(Order(-2, Line(1, "product", "SA015030S"), Line(2, "product", "BAL001V")), Eligible);

        plan.IsEligible.Should().BeTrue();
        plan.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Plan_IgnoresVariantCodeOnNonProductLine()
    {
        var plan = DoubleShotSwapPlanner.Plan(Order(-2, Line(1, "gift", "SA015030V")), Eligible);

        plan.Actions.Should().BeEmpty();
    }
}
