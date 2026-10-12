using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

/// <summary>One V line to replace. <see cref="SetItemToAdd"/> is null when a previous run already
/// added the tagged S line and only the V line still needs deleting (a "heal").</summary>
public sealed record DoubleShotSwapAction(
    long VariantItemId,
    string VariantCode,
    string SetCode,
    decimal Amount,
    NewEshopOrderItem? SetItemToAdd)
{
    public bool IsHeal => SetItemToAdd is null;
}

public sealed record DoubleShotSwapPlan(string OrderCode, bool IsEligible, IReadOnlyList<DoubleShotSwapAction> Actions);

/// <summary>
/// Decides which double-shot V lines of an order must become their S mirror set. Pure: no I/O.
/// The S line is tagged with the V line's id so a swap interrupted between POST and DELETE is
/// finished, never duplicated, by the next run.
/// </summary>
public static class DoubleShotSwapPlanner
{
    public const string ProductItemType = "product";
    public const string ProductSetItemType = "product-set";

    public static string TagFor(string variantCode, long variantItemId) =>
        $"auto-swap {variantCode} #{variantItemId}";

    public static DoubleShotSwapPlan Plan(EshopOrderDetail order, IReadOnlyCollection<int> eligibleStatusIds)
    {
        if (!eligibleStatusIds.Contains(order.StatusId))
            return new DoubleShotSwapPlan(order.Code, IsEligible: false, Actions: []);

        var existingTags = order.Lines
            .Where(l => l.ItemType == ProductSetItemType && !string.IsNullOrEmpty(l.Remark))
            .Select(l => l.Remark!)
            .ToHashSet(StringComparer.Ordinal);

        var actions = order.Lines
            .Where(l => l.ItemType == ProductItemType && DoubleShotCodeRule.IsVariantCode(l.Code))
            .Select(l => BuildAction(l, existingTags))
            .ToList();

        return new DoubleShotSwapPlan(order.Code, IsEligible: true, Actions: actions);
    }

    private static DoubleShotSwapAction BuildAction(EshopOrderLine line, IReadOnlySet<string> existingTags)
    {
        var variantCode = line.Code!;
        DoubleShotCodeRule.TryGetSetCode(variantCode, out var setCode);
        var tag = TagFor(variantCode, line.ItemId);

        var setItem = existingTags.Contains(tag)
            ? null
            : new NewEshopOrderItem
            {
                ItemType = ProductSetItemType,
                Code = setCode,
                Amount = line.Amount,
                UnitPriceWithVat = line.UnitPriceWithVat,
                VatRate = line.VatRate,
                PriceRatio = line.PriceRatio,
                Remark = tag,
            };

        return new DoubleShotSwapAction(line.ItemId, variantCode, setCode, line.Amount, setItem);
    }
}
