using Anela.Heblo.Xcc.Audit;

namespace Anela.Heblo.Application.Features.Audit.Model;

/// <summary>
/// Response containing audit logs
/// </summary>
public class GetAuditLogsResponse
{
    /// <summary>
    /// Number of audit logs returned
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// List of audit log entries
    /// </summary>
    public IReadOnlyList<DataLoadAuditEntry> Logs { get; set; } = new List<DataLoadAuditEntry>();
}