using Anela.Heblo.Domain.Features.Catalog.Sales;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Adds component sales to the sales stream for every bundle sold.
///
/// A bundle sells as a single ERP invoice line carrying the bundle's own product code, so its
/// contents are invisible to manufacturing planning. This expander emits one synthetic record per
/// component per bundle sale, carrying quantity only — revenue stays entirely on the bundle's own
/// record so company totals are unaffected.
///
/// Pure: no I/O, no state, safe to call from the merge path.
/// </summary>
public sealed class BundleSalesExpander
{
    public IReadOnlyList<CatalogSaleRecord> Expand(
        IEnumerable<CatalogSaleRecord> sales,
        IEnumerable<CatalogSetPart> setParts)
    {
        var salesList = sales as IReadOnlyList<CatalogSaleRecord> ?? sales.ToList();

        var partsBySet = setParts
            .GroupBy(p => p.SetCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        if (partsBySet.Count == 0)
            return salesList;

        var expanded = new List<CatalogSaleRecord>(salesList);

        foreach (var sale in salesList)
        {
            // One level only: a record already derived from a bundle is never expanded again.
            if (sale.SourceBundleCode != null)
                continue;

            if (!partsBySet.TryGetValue(sale.ProductCode, out var parts))
                continue;

            foreach (var part in parts)
            {
                var amountB2B = sale.AmountB2B * part.Amount;
                var amountB2C = sale.AmountB2C * part.Amount;

                expanded.Add(new CatalogSaleRecord
                {
                    Date = sale.Date,
                    ProductCode = part.ComponentCode,
                    ProductName = part.ComponentName,
                    AmountB2B = amountB2B,
                    AmountB2C = amountB2C,
                    AmountTotal = amountB2B + amountB2C,
                    SumB2B = 0,
                    SumB2C = 0,
                    SumTotal = 0,
                    SourceBundleCode = sale.ProductCode,
                });
            }
        }

        return expanded;
    }
}
