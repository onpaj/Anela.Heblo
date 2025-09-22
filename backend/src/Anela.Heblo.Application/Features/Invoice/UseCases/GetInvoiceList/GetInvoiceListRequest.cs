using MediatR;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceList;

public class GetInvoiceListRequest : IRequest<GetInvoiceListResponse>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}