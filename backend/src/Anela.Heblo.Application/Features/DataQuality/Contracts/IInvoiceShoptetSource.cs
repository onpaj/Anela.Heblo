namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the Shoptet issued-invoice source.
/// Provider (Invoices) supplies an adapter — see InvoiceShoptetSourceAdapter.
/// </summary>
public interface IInvoiceShoptetSource
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DqtInvoiceSourceQuery query,
        CancellationToken ct = default);
}
