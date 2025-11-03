using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsResponse
{
    public ReceivedInvoiceDto? Invoice { get; set; }
    
    public bool Found { get; set; }
}