using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceDetail;

/// <summary>
/// Request for getting detailed information about an issued invoice including sync history
/// </summary>
public class GetIssuedInvoiceDetailRequest : IRequest<GetIssuedInvoiceDetailResponse>
{
    public string Id { get; set; } = string.Empty;
}