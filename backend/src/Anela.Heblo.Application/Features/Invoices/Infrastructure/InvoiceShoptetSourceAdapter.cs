using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

/// <summary>
/// Provider-side adapter binding the DataQuality contract IInvoiceShoptetSource
/// to the Invoices-module IIssuedInvoiceSource, mapping to/from DataQuality's
/// consumer-owned snapshot contracts via InvoiceDqtSnapshotMapper.
/// </summary>
internal sealed class InvoiceShoptetSourceAdapter : IInvoiceShoptetSource
{
    private readonly IIssuedInvoiceSource _inner;

    public InvoiceShoptetSourceAdapter(IIssuedInvoiceSource inner)
    {
        _inner = inner;
    }

    public async Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DqtInvoiceSourceQuery query,
        CancellationToken ct = default)
    {
        var innerQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = query.RequestId,
            DateFrom = query.DateFrom.ToDateTime(TimeOnly.MinValue),
            DateTo = query.DateTo.ToDateTime(TimeOnly.MinValue)
        };

        var batches = await _inner.GetAllAsync(innerQuery, ct);

        return batches
            .SelectMany(b => b.Invoices)
            .Select(i => i.ToDqtSnapshot())
            .ToList();
    }
}
