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
    // Quantities are doubles summed across rows, so a product whose sales and returns cancel out
    // within the window lands on a floating-point residual instead of an exact zero. Dividing its
    // revenue by 5e-17 pieces once produced a cost of 2,1e17 Kc per piece, so anything under a
    // thousandth of a piece counts as no quantity at all.
    private const double MinimumAllocatablePieces = 0.001;

    /// <summary>
    /// Company-wide sales revenue in the window - the denominator of the allocation rate.
    ///
    /// Only revenue that will actually be charged back counts, which is what keeps the allocation
    /// tied to the ledger: every koruna in this sum belongs to a product that receives a cost, and
    /// every product that receives a cost has its revenue in this sum. A product whose returns
    /// outweighed its sales would otherwise shrink the divisor and inflate the cost of every
    /// product that did sell, while itself being charged nothing.
    /// </summary>
    public static decimal CalculateTotalRevenue(
        IEnumerable<CatalogAggregate> products,
        DateTime from,
        DateTime to)
    {
        return products
            .Where(HasProductCode)
            .Sum(product => CalculateAllocatableRevenue(product, from, to));
    }

    /// <summary>
    /// Turns the allocation rate into a flat cost per piece per product - the same value for every
    /// month of the window, as the providers have always emitted. A product that earned nothing in
    /// the window took no share of the pool and carries zero.
    /// </summary>
    public static Dictionary<string, List<MonthlyCost>> BuildProductCosts(
        IEnumerable<CatalogAggregate> products,
        decimal costPerRevenueUnit,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<DateTime> months)
    {
        var productCosts = new Dictionary<string, List<MonthlyCost>>();

        foreach (var product in products.Where(HasProductCode))
        {
            var (pieces, revenue) = SumSalesInWindow(product, from, to);
            var costPerPiece = IsAllocatable(pieces, revenue)
                ? costPerRevenueUnit * (revenue / (decimal)pieces)
                : 0m;

            productCosts[product.ProductCode!] = months.Select(m => new MonthlyCost(m, costPerPiece)).ToList();
        }

        return productCosts;
    }

    private static decimal CalculateAllocatableRevenue(
        CatalogAggregate product,
        DateTime from,
        DateTime to)
    {
        var (pieces, revenue) = SumSalesInWindow(product, from, to);

        return IsAllocatable(pieces, revenue) ? revenue : 0m;
    }

    /// <summary>
    /// Whether this product can take a share of the pool at all. Returns can outweigh sales in a
    /// short window, and a row can carry revenue with no quantity; neither is a share worth
    /// charging anybody, and both sides of the allocation have to agree on that.
    /// </summary>
    private static bool IsAllocatable(double pieces, decimal revenue)
        => pieces > MinimumAllocatablePieces && revenue > 0m;

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

    private static bool HasProductCode(CatalogAggregate product)
        => !string.IsNullOrEmpty(product.ProductCode);
}
