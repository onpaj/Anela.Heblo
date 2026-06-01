namespace Anela.Heblo.Persistence.Analytics.Entities;

public class Contact
{
    public long FlexiId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Cin { get; set; }
    public string? Vatin { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
