using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceConsumptionSourceAdapter : IInvoiceConsumptionSource
{
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceConsumptionSourceAdapter(IIssuedInvoiceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var invoices = await _repository.GetHeadersByDateAsync(date, cancellationToken);
        return invoices
            .Select(invoice => new InvoiceConsumptionHeader(invoice.Id, invoice.ItemsCount))
            .ToList();
    }
}
