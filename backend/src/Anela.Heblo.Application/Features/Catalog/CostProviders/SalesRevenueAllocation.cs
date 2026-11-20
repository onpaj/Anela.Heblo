using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;

namespace Anela.Heblo.Application.Features.Catalog.CostProviders;

/// <summary>
/// The allocation M2 and M3 share: a pool is spread across products in proportion to the revenue
/// each one earned, not to the number of pieces it sold. A 4,9ml deodorant therefore no longer
/// carries the same warehouse and overhead cost as a 180ml cream.
///
/// The per-product figure stays a cost per piece, because that is what the margin cascade
/// subtracts from the unit selling price:
///
///     sazba na korunu trzby = pool / celkove trzby
///     naklad na kus         = sazba × trzba na kus produktu
///
/// Both levels allocate through the single <see cref="Allocate"/> entry point, so the
/// comparability the providers document ("M2 and M3 divide the same denominator") cannot drift
/// apart in one copy of the logic - including the rule for who is allocatable at all, which has to
/// hold on both sides of the division or the pool stops adding up.
/// </summary>
internal static class SalesRevenueAllocation
{
    // Quantities are doubles summed across rows, so a product whose sales and returns cancel out
    // within the window lands on a floating-point residual instead of an exact zero. Dividing its
    // revenue by 5e-17 pieces once produced a cost of 2,1e17 Kc per piece, so anything under a
    // thousandth of a piece counts as no quantity at all.
    private const double MinimumAllocatablePieces = 0.001;

    /// <summary>
    /// What one provider needs back: the flat cost per piece for every product across every month
    /// of the window, plus whether anything was actually earned in it. The providers log that in
    /// their own voice, which is the only part of the step that differs between M2 and M3.
    /// </summary>
    internal readonly record struct AllocationResult(
        Dictionary<string, List<MonthlyCost>> ProductCosts,
        bool HasAllocatableRevenue);

    /// <summary>
    /// Spreads <paramref name="pool"/> across <paramref name="products"/> in proportion to the
    /// revenue each earned in the window. With nothing earned, every product carries zero rather
    /// than the whole pool being dumped on whoever happens to be first.
    /// </summary>
    public static AllocationResult Allocate(
        decimal pool,
        IReadOnlyCollection<CatalogAggregate> products,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<DateTime> months)
    {
        // One pass over each product's sales history, not one per side of the division.
        var sales = SumSalesInWindow(products, from, to);

        var totalRevenue = sales.Values
            .Where(s => s.IsAllocatable)
            .Sum(s => s.Revenue);

        var hasAllocatableRevenue = totalRevenue > 0m;
        var costPerRevenueUnit = hasAllocatableRevenue ? pool / totalRevenue : 0m;

        var productCosts = sales.ToDictionary(
            entry => entry.Key,
            entry => BuildMonthlyCosts(entry.Value, costPerRevenueUnit, months));

        return new AllocationResult(productCosts, hasAllocatableRevenue);
    }

    /// <summary>
    /// A flat cost per piece repeated across every month of the window, as the providers have
    /// always emitted. A product that earned nothing took no share of the pool and carries zero.
    /// </summary>
    private static List<MonthlyCost> BuildMonthlyCosts(
        ProductSales sales,
        decimal costPerRevenueUnit,
        IReadOnlyCollection<DateTime> months)
    {
        var costPerPiece = sales.IsAllocatable
            ? costPerRevenueUnit * (sales.Revenue / (decimal)sales.Pieces)
            : 0m;

        return months.Select(month => new MonthlyCost(month, costPerPiece)).ToList();
    }

    /// <summary>
    /// Pieces and revenue per product in the window. Synthetic rows expanded from a bundle sale
    /// (<see cref="Domain.Features.Catalog.Sales.CatalogSaleRecord.SourceBundleCode"/>) carry
    /// quantity but no revenue - the bundle keeps that - so counting them would sink the
    /// component's revenue per piece and leave it carrying almost no cost.
    /// </summary>
    private static Dictionary<string, ProductSales> SumSalesInWindow(
        IEnumerable<CatalogAggregate> products,
        DateTime from,
        DateTime to)
    {
        var sales = new Dictionary<string, ProductSales>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            var pieces = 0d;
            var revenue = 0m;

            foreach (var sale in product.SalesHistory)
            {
                if (sale.Date < from || sale.Date > to || sale.SourceBundleCode != null)
                    continue;

                pieces += sale.AmountTotal;
                revenue += sale.SumTotal;
            }

            sales[product.ProductCode] = new ProductSales(pieces, revenue);
        }

        return sales;
    }

    private readonly record struct ProductSales(double Pieces, decimal Revenue)
    {
        /// <summary>
        /// Whether this product can take a share of the pool at all. Returns can outweigh sales in
        /// a short window, and a row can carry revenue with no quantity; neither is a share worth
        /// charging anybody, and both sides of the allocation have to agree on that.
        /// </summary>
        public bool IsAllocatable => Pieces > MinimumAllocatablePieces && Revenue > 0m;
    }
}
