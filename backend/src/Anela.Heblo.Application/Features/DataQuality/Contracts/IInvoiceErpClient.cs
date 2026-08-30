namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract over the ERP issued-invoice client.
/// Provider (Invoices) supplies an adapter — see InvoiceErpClientAdapter.
/// </summary>
public interface IInvoiceErpClient
{
    Task<List<DqtInvoiceSnapshot>> GetAllAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
