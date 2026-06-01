using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;

/// <summary>
/// Response with paginated list of issued invoices
/// </summary>
public class GetIssuedInvoicesListResponse : BaseResponse
{
    public List<IssuedInvoiceDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}