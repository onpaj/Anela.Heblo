namespace Anela.Heblo.Application.Domain.IssuedInvoices;

public interface IIssuedInvoiceSource
{
    Task<List<IssuedInvoiceDetailBatch>> GetAllAsync(IssuedInvoiceSourceQuery query);

    Task CommitAsync(IssuedInvoiceDetailBatch batch, string? commitMessage = default);

    Task FailAsync(IssuedInvoiceDetailBatch batch, string? errorMessage = default);
}