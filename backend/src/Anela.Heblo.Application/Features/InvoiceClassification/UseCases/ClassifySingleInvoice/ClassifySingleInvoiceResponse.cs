using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifySingleInvoice;

public class ClassifySingleInvoiceResponse : BaseResponse
{
    public ClassificationResult Result { get; set; }
    
    public string? AppliedRule { get; set; }
    
    public string? AccountingTemplateCode { get; set; }
    
    public string? ErrorMessage { get; set; }

    public ClassifySingleInvoiceResponse() : base() { }

    public ClassifySingleInvoiceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters) { }
}