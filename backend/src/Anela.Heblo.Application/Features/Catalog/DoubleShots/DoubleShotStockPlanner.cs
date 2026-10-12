using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.DoubleShots;

public sealed record DoubleShotStockAdjustment(string VariantCode, string SetCode, decimal Current, decimal Target)
{
    public decimal Delta => Target - Current;
}

/// <param name="Pairs">Every V↔S pair found, including those whose CSV values already match.</param>
/// <param name="Adjustments">Pairs whose CSV variant stock differs from the target.</param>
public sealed record DoubleShotStockPlan(
    IReadOnlyList<DoubleShotStockAdjustment> Pairs,
    IReadOnlyList<DoubleShotStockAdjustment> Adjustments,
    IReadOnlyList<string> UnpairedVariantCodes)
{
    public int PairCount => Pairs.Count;
}

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

        var pairs = new List<DoubleShotStockAdjustment>();
        var unpaired = new List<string>();

        foreach (var (code, current) in stockByCode)
        {
            if (!DoubleShotCodeRule.TryGetSetCode(code, out var setCode))
                continue;

            if (!stockByCode.TryGetValue(setCode, out var setStock))
            {
                unpaired.Add(code);
                continue;
            }

            pairs.Add(new DoubleShotStockAdjustment(code, setCode, current, Math.Max(setStock, 0m)));
        }

        var adjustments = pairs.Where(p => p.Delta != 0m).ToList();
        return new DoubleShotStockPlan(pairs, adjustments, unpaired);
    }
}
