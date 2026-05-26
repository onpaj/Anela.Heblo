namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoiceItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
