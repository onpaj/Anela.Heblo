namespace Anela.Heblo.Application.Domain.IssuedInvoices;

public class IssuedInvoiceDetailBatch
{
    public List<IssuedInvoiceDetail> Invoices { get; set; } = new();
    public string BatchId { get; set; } = string.Empty;
}