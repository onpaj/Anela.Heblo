namespace Anela.Heblo.Persistence.Analytics.Entities;

public class LedgerEntry
{
    public long FlexiId { get; set; }
    public string? Code { get; set; }
    public DateOnly EntryDate { get; set; }
    public string? Period { get; set; }
    public string? DocumentType { get; set; }
    public string? AccountDebit { get; set; }
    public string? AccountCredit { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? CostCenter { get; set; }
    public string? Contact { get; set; }
    public string? AccountingTemplate { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
