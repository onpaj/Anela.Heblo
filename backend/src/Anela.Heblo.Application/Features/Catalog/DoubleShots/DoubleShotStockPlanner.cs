using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.DoubleShots;

public sealed record DoubleShotStockAdjustment(string VariantCode, string SetCode, decimal Current, decimal Target)
{
    public decimal Delta => Target - Current;
}

public sealed record DoubleShotStockPlan(
    IReadOnlyList<DoubleShotStockAdjustment> Adjustments,
    int PairCount,
    IReadOnlyList<string> UnpairedVariantCodes);

/// <summary>
/// Pairs each double-shot variant (<c>SA…V</c>) with its mirror set (<c>SA…S</c>) in the Shoptet
/// stock export and computes the variant's target stock = max(set stock, 0).
/// </summary>
public static class DoubleShotStockPlanner
{
    public static DoubleShotStockPlan Plan(IReadOnlyList<EshopStock> stock)
    {
        var stockByCode = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in stock)
        {
            if (!string.IsNullOrEmpty(row.Code))
                stockByCode.TryAdd(row.Code, row.Stock);
        }

        var adjustments = new List<DoubleShotStockAdjustment>();
        var unpaired = new List<string>();
        var pairCount = 0;

        foreach (var (code, current) in stockByCode)
        {
            if (!DoubleShotCodeRule.TryGetSetCode(code, out var setCode))
                continue;

            if (!stockByCode.TryGetValue(setCode, out var setStock))
            {
                unpaired.Add(code);
                continue;
            }

            pairCount++;
            var target = Math.Max(setStock, 0m);
            if (target != current)
                adjustments.Add(new DoubleShotStockAdjustment(code, setCode, current, target));
        }

        return new DoubleShotStockPlan(adjustments, pairCount, unpaired);
    }
}
