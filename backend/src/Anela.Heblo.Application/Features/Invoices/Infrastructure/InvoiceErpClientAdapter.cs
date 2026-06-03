using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-side adapter binding the DataQuality contract IInvoiceErpClient
/// to the Invoices-module IIssuedInvoiceClient. Pure delegation, no business logic.
/// </summary>
internal sealed class InvoiceErpClientAdapter : IInvoiceErpClient
{
    private readonly IIssuedInvoiceClient _inner;

    public InvoiceErpClientAdapter(IIssuedInvoiceClient inner)
    {
        _inner = inner;
    }

    public Task<List<IssuedInvoiceDetail>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
        => _inner.GetAllAsync(from, to, ct);
}
