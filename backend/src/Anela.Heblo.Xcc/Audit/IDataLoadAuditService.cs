namespace Anela.Heblo.Xcc.Audit;

public interface IDataLoadAuditService
{
    Task LogDataLoadAsync(string dataType, string source, int recordCount, bool success, Dictionary<string, object>? parameters = null, string? errorMessage = null, TimeSpan? duration = null);
    Task<IReadOnlyList<DataLoadAuditEntry>> GetAuditLogsAsync(int? limit = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<DataLoadAuditEntry> StartDataLoadAuditAsync(string dataType, string source, Dictionary<string, object>? parameters = null);
    Task CompleteDataLoadAuditAsync(string auditId, int recordCount, bool success, string? errorMessage = null);
}