using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-side adapter binding the DataQuality contract IInvoiceShoptetSource
/// to the Invoices-module IIssuedInvoiceSource. Pure delegation, no business logic.
/// </summary>
internal sealed class InvoiceShoptetSourceAdapter : IInvoiceShoptetSource
{
    private readonly IIssuedInvoiceSource _inner;

    public InvoiceShoptetSourceAdapter(IIssuedInvoiceSource inner)
    {
        _inner = inner;
    }

    public Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(
        IssuedInvoiceSourceQuery query,
        CancellationToken ct = default)
        => _inner.GetAllAsync(query, ct);
}
