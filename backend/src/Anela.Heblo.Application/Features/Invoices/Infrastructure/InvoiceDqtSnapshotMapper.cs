using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-owned mapping from Invoices domain types to DataQuality's consumer-owned
/// snapshot contracts (DqtInvoiceSnapshot/DqtInvoiceItem). Shared by InvoiceShoptetSourceAdapter
/// and InvoiceErpClientAdapter so the mapping is written once, not duplicated per adapter.
/// </summary>
internal static class InvoiceDqtSnapshotMapper
{
    public static DqtInvoiceSnapshot ToDqtSnapshot(this IssuedInvoiceDetail invoice)
    {
        return new DqtInvoiceSnapshot
        {
            Code = invoice.Code,
            TotalWithVat = invoice.Price.TotalWithVat,
            TotalWithoutVat = invoice.Price.TotalWithoutVat,
            Items = invoice.Items.Select(ToDqtItem).ToList()
        };
    }

    public static DqtInvoiceItem ToDqtItem(this IssuedInvoiceDetailItem item)
    {
        return new DqtInvoiceItem
        {
            Code = item.Code,
            Amount = item.Amount,
            WithVat = item.ItemPrice.WithVat,
            WithoutVat = item.ItemPrice.WithoutVat
        };
    }
}
