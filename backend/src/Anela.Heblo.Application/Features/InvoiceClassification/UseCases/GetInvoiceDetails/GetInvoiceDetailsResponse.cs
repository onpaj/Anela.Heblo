using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsResponse : BaseResponse
{
    public ReceivedInvoiceDto? Invoice { get; set; }
    
    public bool Found { get; set; }

    public GetInvoiceDetailsResponse() : base() { }

    public GetInvoiceDetailsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) 
        : base(errorCode, parameters) { }
}