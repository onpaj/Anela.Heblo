namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

/// <summary>
/// PackingMaterials-owned read-only abstraction over invoice headers for a
/// single processing date. Implemented by the Invoices module via an adapter
/// (see <c>InvoiceConsumptionSourceAdapter</c>) per the cross-module
/// communication pattern in <c>docs/architecture/development_guidelines.md</c>.
/// </summary>
public interface IInvoiceConsumptionSource
{
    /// <summary>
    /// Returns the materialized list of invoice headers whose invoice date
    /// falls on <paramref name="date"/>. Each header carries only the fields
    /// required by <c>ConsumptionCalculationService.BuildFactRows</c>.
    /// </summary>
    Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}
