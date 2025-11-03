namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoiceDto
{
    public string InvoiceNumber { get; set; } = string.Empty;
    
    public string CompanyName { get; set; } = string.Empty;
    
    public string CompanyVat { get; set; } = string.Empty;
    
    public DateTime? InvoiceDate { get; set; }
    
    public decimal TotalAmount { get; set; }
    
    public string Description { get; set; } = string.Empty;
    
    public List<ReceivedInvoiceItemDto> Items { get; set; } = new();
    public DateTime? DueDate { get; set; }
    public string? AccountingTemplateCode { get; set; }
    public string? DepartmentCode { get; set; }
    public string[] Labels { get; set; }
}