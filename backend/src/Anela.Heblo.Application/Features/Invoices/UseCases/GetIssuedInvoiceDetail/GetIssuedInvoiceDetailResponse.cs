using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;

/// <summary>
/// Response with detailed information about an issued invoice
/// </summary>
public class GetIssuedInvoiceDetailResponse : BaseResponse
{
    public IssuedInvoiceDetailDto? Invoice { get; set; }
}