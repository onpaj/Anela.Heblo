namespace Anela.Heblo.Domain.Features.Invoices;

/// <summary>
/// Statistics for invoice synchronization
/// </summary>
public class IssuedInvoiceSyncStats
{
    public int TotalInvoices { get; set; }
    public int SyncedInvoices { get; set; }
    public int UnsyncedInvoices { get; set; }
    public int InvoicesWithErrors { get; set; }
    public int CriticalErrors { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public decimal SyncSuccessRate => TotalInvoices > 0 ? (decimal)SyncedInvoices / TotalInvoices * 100 : 0;
}
