using Anela.Heblo.Application.Features.Invoice.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoice.UseCases.GetInvoiceList;

public class GetInvoiceListResponse : BaseResponse
{
    public List<InvoiceDto> Invoices { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}