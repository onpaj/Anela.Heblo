namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoiceItemDto
{
    public string Description { get; set; } = string.Empty;
    
    public decimal Quantity { get; set; }
    
    public decimal UnitPrice { get; set; }
    
    public decimal TotalPrice { get; set; }
}