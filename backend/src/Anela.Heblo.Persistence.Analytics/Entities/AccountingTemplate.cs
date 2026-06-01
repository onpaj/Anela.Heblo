namespace Anela.Heblo.Persistence.Analytics.Entities;

public class AccountingTemplate
{
    public long FlexiId { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
