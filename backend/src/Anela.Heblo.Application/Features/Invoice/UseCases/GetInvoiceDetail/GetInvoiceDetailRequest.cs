using MediatR;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceDetail;

public class GetInvoiceDetailRequest : IRequest<GetInvoiceDetailResponse>
{
    public string ExternalId { get; set; } = null!;
}