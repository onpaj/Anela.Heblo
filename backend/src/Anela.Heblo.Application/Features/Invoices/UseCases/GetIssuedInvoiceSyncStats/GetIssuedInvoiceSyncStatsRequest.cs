using MediatR;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;

/// <summary>
/// Request for getting issued invoice synchronization statistics
/// </summary>
public class GetIssuedInvoiceSyncStatsRequest : IRequest<GetIssuedInvoiceSyncStatsResponse>
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}