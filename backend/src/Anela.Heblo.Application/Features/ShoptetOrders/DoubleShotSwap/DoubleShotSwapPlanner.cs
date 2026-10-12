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

/// <summary>A V line left alone because its amount or unit price is not positive: swapping it would
/// replace a paid line with a free S line (or copy a nonsense amount).</summary>
public sealed record DoubleShotSkippedLine(long ItemId, string VariantCode, decimal Amount, decimal UnitPriceWithVat);

public sealed record DoubleShotSwapPlan(
    string OrderCode,
    bool IsEligible,
    IReadOnlyList<DoubleShotSwapAction> Actions,
    IReadOnlyList<DoubleShotSkippedLine> Skipped);

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
            return new DoubleShotSwapPlan(order.Code, IsEligible: false, Actions: [], Skipped: []);

        var existingTags = order.Lines
            .Where(l => l.ItemType == ProductSetItemType && !string.IsNullOrEmpty(l.Remark))
            .Select(l => l.Remark!)
            .ToHashSet(StringComparer.Ordinal);

        var variantLines = order.Lines
            .Where(l => l.ItemType == ProductItemType && DoubleShotCodeRule.IsVariantCode(l.Code))
            .ToList();

        return new DoubleShotSwapPlan(
            order.Code,
            IsEligible: true,
            Actions: variantLines.Where(l => ShouldSwap(l, existingTags)).Select(l => BuildAction(l, existingTags)).ToList(),
            Skipped: variantLines.Where(l => !ShouldSwap(l, existingTags))
                .Select(l => new DoubleShotSkippedLine(l.ItemId, l.Code!, l.Amount, l.UnitPriceWithVat)).ToList());
    }

    /// <summary>A new S line is only added for a positive amount and price, so a paid V line is never
    /// replaced by a free S line. A heal always proceeds: its S line already exists, and keeping the V
    /// line would double the stock deduction.</summary>
    private static bool ShouldSwap(EshopOrderLine line, IReadOnlySet<string> existingTags) =>
        existingTags.Contains(TagFor(line.Code!, line.ItemId))
        || (line.Amount > 0m && line.UnitPriceWithVat > 0m);

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
