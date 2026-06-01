namespace Anela.Heblo.Domain.Features.Invoices;

public class IssuedInvoiceDetailBatch
{
    public List<IssuedInvoiceDetail> Invoices { get; set; } = new();
    public string BatchId { get; set; } = string.Empty;
}