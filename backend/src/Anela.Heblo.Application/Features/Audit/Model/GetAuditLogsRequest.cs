using MediatR;

namespace Anela.Heblo.Application.Features.Audit.Model;

/// <summary>
/// Request for getting data load audit logs
/// </summary>
public class GetAuditLogsRequest : IRequest<GetAuditLogsResponse>
{
    /// <summary>
    /// Maximum number of logs to return (default: 100)
    /// </summary>
    public int? Limit { get; set; } = 100;

    /// <summary>
    /// Filter logs from this date (inclusive)
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Filter logs to this date (inclusive)
    /// </summary>
    public DateTime? ToDate { get; set; }
}