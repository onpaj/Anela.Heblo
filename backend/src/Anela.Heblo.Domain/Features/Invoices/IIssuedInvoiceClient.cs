namespace Anela.Heblo.Domain.Features.Invoices;

/// <summary>
/// Interface for issued invoice client to save invoices to external system (e.g., ABRA Flexi)
/// </summary>
public interface IIssuedInvoiceClient
{
    Task<string?> SaveAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken = default);
    Task<IssuedInvoiceDetail> GetAsync(string invoiceId, CancellationToken cancellationToken = default);
    Task<List<IssuedInvoiceDetail>> GetAllAsync(DateOnly from, DateOnly to, CancellationToken ct);
}



