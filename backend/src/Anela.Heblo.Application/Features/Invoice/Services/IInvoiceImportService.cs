using Anela.Heblo.Application.Features.Invoice.UseCases.ImportInvoices;
using Anela.Heblo.Domain.Features.Invoice;

namespace Anela.Heblo.Application.Features.Invoice.Services;

public interface IInvoiceImportService
{
    Task<BatchImportResult> ImportBatchAsync(BatchCriteria? criteria, CancellationToken cancellationToken = default);
}

public class BatchImportResult
{
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<ImportAttempt> Attempts { get; set; } = new();
}