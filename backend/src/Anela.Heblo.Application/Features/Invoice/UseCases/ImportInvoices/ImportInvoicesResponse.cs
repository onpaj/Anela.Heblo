using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.ImportInvoices;

public class ImportInvoicesResponse : BaseResponse
{
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<ImportAttemptDto> ImportAttempts { get; set; } = new();
}

public class EnqueueImportResponse : BaseResponse
{
    public string JobId { get; set; } = null!;
    public string Message { get; set; } = "Import queued for processing";
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
}