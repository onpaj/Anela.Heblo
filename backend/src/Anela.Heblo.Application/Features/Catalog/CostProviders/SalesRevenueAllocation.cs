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
/// Both levels allocate through this one place, so the comparability the providers document
/// ("M2 and M3 divide the same denominator") cannot drift apart in one copy of the logic.
/// </summary>
internal static class SalesRevenueAllocation
{
    /// <summary>
    /// Company-wide sales revenue in the window - the denominator of the allocation rate.
    /// </summary>
    public static decimal CalculateTotalRevenue(
        IEnumerable<CatalogAggregate> products,
        DateTime from,
        DateTime to)
    {
        return products.Sum(product => SumSalesInWindow(product, from, to).revenue);
    }

    /// <summary>
    /// Turns the allocation rate into a flat cost per piece per product - the same value for every
    /// month of the window, as the providers have always emitted. A product that sold nothing in
    /// the window earned no share of the pool and carries zero.
    /// </summary>
    public static Dictionary<string, List<MonthlyCost>> BuildProductCosts(
        IEnumerable<CatalogAggregate> products,
        decimal costPerRevenueUnit,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<DateTime> months)
    {
        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products)
        {
            if (string.IsNullOrEmpty(product.ProductCode))
                continue;

            var costPerPiece = CalculateCostPerPiece(product, costPerRevenueUnit, from, to);
            productCosts[product.ProductCode] = months.Select(m => new MonthlyCost(m, costPerPiece)).ToList();
        }

        return productCosts;
    }

    private static decimal CalculateCostPerPiece(
        CatalogAggregate product,
        decimal costPerRevenueUnit,
        DateTime from,
        DateTime to)
    {
        var (pieces, revenue) = SumSalesInWindow(product, from, to);

        // Returns can outweigh sales in a short window, and a row can carry revenue with no
        // quantity; neither is a share of the pool worth charging anybody.
        if (pieces <= 0 || revenue <= 0)
        {
            return 0m;
        }

        return costPerRevenueUnit * (revenue / (decimal)pieces);
    }

    /// <summary>
    /// Pieces and revenue of one product in the window. Synthetic rows expanded from a bundle sale
    /// (<see cref="Domain.Features.Catalog.Sales.CatalogSaleRecord.SourceBundleCode"/>) carry
    /// quantity but no revenue - the bundle keeps that - so counting them would sink the
    /// component's revenue per piece and leave it carrying almost no cost.
    /// </summary>
    private static (double pieces, decimal revenue) SumSalesInWindow(
        CatalogAggregate product,
        DateTime from,
        DateTime to)
    {
        var pieces = 0d;
        var revenue = 0m;

        foreach (var sale in product.SalesHistory)
        {
            if (sale.Date < from || sale.Date > to || sale.SourceBundleCode != null)
                continue;

            pieces += sale.AmountTotal;
            revenue += sale.SumTotal;
        }

        return (pieces, revenue);
    }
}
