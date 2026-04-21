namespace Anela.Heblo.Domain.Features.Invoices;

public interface IIssuedInvoiceSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query, CancellationToken cancellationToken = default);

    Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default);

    Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default);
}