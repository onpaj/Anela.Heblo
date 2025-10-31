namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoiceDto
{
    public string Id { get; set; } = string.Empty;
    
    public string InvoiceNumber { get; set; } = string.Empty;
    
    public string CompanyName { get; set; } = string.Empty;
    
    public string CompanyIco { get; set; } = string.Empty;
    
    public DateTime InvoiceDate { get; set; }
    
    public decimal TotalAmount { get; set; }
    
    public string Description { get; set; } = string.Empty;
    
    public List<ReceivedInvoiceItemDto> Items { get; set; } = new();
}