using MediatR;

namespace Anela.Heblo.Application.Features.Audit.Contracts;

/// <summary>
/// Request for getting audit summary statistics
/// </summary>
public class GetAuditSummaryRequest : IRequest<GetAuditSummaryResponse>
{
    /// <summary>
    /// Filter summary from this date (inclusive)
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Filter summary to this date (inclusive)
    /// </summary>
    public DateTime? ToDate { get; set; }
}