using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;

/// <summary>
/// Response for issued invoice synchronization statistics
/// </summary>
public class GetIssuedInvoiceSyncStatsResponse : BaseResponse
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate { get; set; }
}