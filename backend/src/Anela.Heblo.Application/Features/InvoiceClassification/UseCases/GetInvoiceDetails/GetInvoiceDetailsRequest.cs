using MediatR;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsRequest : IRequest<GetInvoiceDetailsResponse>
{
    public string InvoiceId { get; set; } = string.Empty;
}