namespace Anela.Heblo.Application.Features.Invoices.Contracts;

public class ImportInvoiceRequestDto
{
    public List<string> InvoiceIds { get; set; } = new();
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? Currency { get; set; }
    public bool TryUnpairIfNecessary { get; set; }
}