namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// Campaign / ad group / keyword / negative keyword / ad. <see cref="Level"/> and <see cref="Status"/>
/// hold the AdEntityLevel / AdEntityStatus enum names. <see cref="AttributesJson"/> is jsonb with the
/// platform-specific fields. Unique (account_id, level, external_id).
/// </summary>
public class AdEntity
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string Level { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public long? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string AttributesJson { get; set; } = "{}";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
