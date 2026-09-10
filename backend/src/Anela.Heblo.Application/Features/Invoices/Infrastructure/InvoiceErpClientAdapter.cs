using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-side adapter binding the DataQuality contract IInvoiceErpClient
/// to the Invoices-module IIssuedInvoiceClient, mapping to DataQuality's
/// consumer-owned snapshot contracts via InvoiceDqtSnapshotMapper.
/// </summary>
internal sealed class InvoiceErpClientAdapter : IInvoiceErpClient
{
    private readonly IIssuedInvoiceClient _inner;

    public InvoiceErpClientAdapter(IIssuedInvoiceClient inner)
    {
        _inner = inner;
    }

    public async Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var invoices = await _inner.GetAllAsync(from, to, ct);

        return invoices
            .Select(i => i.ToDqtSnapshot())
            .ToList();
    }
}
