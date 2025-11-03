using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifySingleInvoice;

public class ClassifySingleInvoiceResponse
{
    public bool Success { get; set; }
    
    public ClassificationResult Result { get; set; }
    
    public string? AppliedRule { get; set; }
    
    public string? AccountingTemplateCode { get; set; }
    
    public string? ErrorMessage { get; set; }
}