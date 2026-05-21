using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class MockInvoiceConsumptionSource : IInvoiceConsumptionSource
{
    private readonly Dictionary<DateOnly, List<InvoiceConsumptionHeader>> _byDate = new();

    public void SetHeaders(DateOnly date, IEnumerable<InvoiceConsumptionHeader> headers)
    {
        _byDate[date] = headers.ToList();
    }

    public Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InvoiceConsumptionHeader> result =
            _byDate.TryGetValue(date, out var headers) ? headers : new List<InvoiceConsumptionHeader>();
        return Task.FromResult(result);
    }
}
