using Anela.Heblo.Domain.Features.DataQuality;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class InvoiceDqtComparer : IInvoiceDqtComparer
{
    private const decimal Tolerance = 0.02m;

    private readonly IIssuedInvoiceSource _shoptetSource;
    private readonly IIssuedInvoiceClient _flexiClient;

    public InvoiceDqtComparer(IIssuedInvoiceSource shoptetSource, IIssuedInvoiceClient flexiClient)
    {
        _shoptetSource = shoptetSource;
        _flexiClient = flexiClient;
    }

    public async Task<InvoiceDqtComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var shoptetQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = $"dqt-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}",
            DateFrom = from.ToDateTime(TimeOnly.MinValue),
            DateTo = to.ToDateTime(TimeOnly.MinValue)
        };

        var shoptetBatches = await _shoptetSource.GetAllAsync(shoptetQuery, ct);
        var shoptetInvoices = shoptetBatches.SelectMany(b => b.Invoices).ToList();

        var flexiInvoices = await _flexiClient.GetAllAsync(from, to, ct);

        var shoptetByCode = shoptetInvoices.ToDictionary(i => i.Code);
        var flexiByCode = flexiInvoices.ToDictionary(i => i.Code);

        var allCodes = shoptetByCode.Keys.Union(flexiByCode.Keys).ToHashSet();
        var mismatches = new List<InvoiceDqtMismatch>();

        foreach (var code in allCodes)
        {
            var inShoptet = shoptetByCode.TryGetValue(code, out var shoptetInvoice);
            var inFlexi = flexiByCode.TryGetValue(code, out var flexiInvoice);

            if (inShoptet && !inFlexi)
            {
                mismatches.Add(new InvoiceDqtMismatch
                {
                    InvoiceCode = code,
                    MismatchType = InvoiceMismatchType.MissingInFlexi
                });
                continue;
            }

            if (!inShoptet && inFlexi)
            {
                mismatches.Add(new InvoiceDqtMismatch
                {
                    InvoiceCode = code,
                    MismatchType = InvoiceMismatchType.MissingInShoptet
                });
                continue;
            }

            // Both exist — compare
            var flags = InvoiceMismatchType.None;
            string? shoptetVal = null;
            string? flexiVal = null;
            string? details = null;

            if (Math.Abs(shoptetInvoice!.Price.TotalWithVat - flexiInvoice!.Price.TotalWithVat) > Tolerance)
            {
                flags |= InvoiceMismatchType.TotalWithVatDiffers;
                shoptetVal = shoptetInvoice.Price.TotalWithVat.ToString("F2");
                flexiVal = flexiInvoice.Price.TotalWithVat.ToString("F2");
            }

            if (Math.Abs(shoptetInvoice.Price.TotalWithoutVat - flexiInvoice.Price.TotalWithoutVat) > Tolerance)
            {
                flags |= InvoiceMismatchType.TotalWithoutVatDiffers;
                shoptetVal ??= shoptetInvoice.Price.TotalWithoutVat.ToString("F2");
                flexiVal ??= flexiInvoice.Price.TotalWithoutVat.ToString("F2");
            }

            var itemDiff = CompareItems(shoptetInvoice.Items, flexiInvoice.Items);
            if (itemDiff != null)
            {
                flags |= InvoiceMismatchType.ItemsDiffer;
                details = itemDiff;
            }

            if (flags != InvoiceMismatchType.None)
            {
                mismatches.Add(new InvoiceDqtMismatch
                {
                    InvoiceCode = code,
                    MismatchType = flags,
                    ShoptetValue = shoptetVal,
                    FlexiValue = flexiVal,
                    Details = details
                });
            }
        }

        return new InvoiceDqtComparisonResult
        {
            Mismatches = mismatches,
            TotalChecked = allCodes.Count
        };
    }

    private static string? CompareItems(List<IssuedInvoiceDetailItem> shoptetItems, List<IssuedInvoiceDetailItem> flexiItems)
    {
        var shoptetByCode = shoptetItems.ToDictionary(i => i.Code);
        var flexiByCode = flexiItems.ToDictionary(i => i.Code);
        var allCodes = shoptetByCode.Keys.Union(flexiByCode.Keys);

        var diffs = new List<string>();

        foreach (var code in allCodes)
        {
            var inShoptet = shoptetByCode.TryGetValue(code, out var sItem);
            var inFlexi = flexiByCode.TryGetValue(code, out var fItem);

            if (inShoptet && !inFlexi)
            {
                diffs.Add($"Item {code}: missing in Flexi");
                continue;
            }

            if (!inShoptet && inFlexi)
            {
                diffs.Add($"Item {code}: missing in Shoptet");
                continue;
            }

            if (sItem!.Amount != fItem!.Amount)
                diffs.Add($"Item {code}: Amount shoptet={sItem.Amount} flexi={fItem.Amount}");

            if (Math.Abs(sItem.ItemPrice.WithVat - fItem.ItemPrice.WithVat) > Tolerance)
                diffs.Add($"Item {code}: WithVat shoptet={sItem.ItemPrice.WithVat:F2} flexi={fItem.ItemPrice.WithVat:F2}");

            if (Math.Abs(sItem.ItemPrice.WithoutVat - fItem.ItemPrice.WithoutVat) > Tolerance)
                diffs.Add($"Item {code}: WithoutVat shoptet={sItem.ItemPrice.WithoutVat:F2} flexi={fItem.ItemPrice.WithoutVat:F2}");
        }

        return diffs.Count > 0 ? string.Join("; ", diffs) : null;
    }
}
