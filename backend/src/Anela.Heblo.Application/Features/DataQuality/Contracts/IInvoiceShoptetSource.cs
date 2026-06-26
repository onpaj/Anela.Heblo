using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the Shoptet issued-invoice source.
/// Provider (Invoices) supplies an adapter — see InvoiceShoptetSourceAdapter.
/// </summary>
public interface IInvoiceShoptetSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default);
}
