namespace Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueInvoiceImport;

public class EnqueueInvoiceImportResponse
{
    public List<string> JobIds { get; set; } = new();
}