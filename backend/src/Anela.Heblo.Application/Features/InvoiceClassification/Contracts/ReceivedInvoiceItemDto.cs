namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class ReceivedInvoiceItemDto
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
