namespace Anela.Heblo.Persistence.Ads.Entities;

/// <summary>
/// One change to an ad account, from the platform change log or the snapshot-diff fallback (C2).
/// <see cref="ActorKind"/> holds the AdChangeActorKind name; <see cref="Source"/> an
/// <see cref="AdChangeSources"/> value; <see cref="Origin"/> an <see cref="AdChangeOrigins"/> value.
/// <see cref="MatchedExecutionId"/> references the C3 execution record (ApplicationDbContext, public
/// schema) as text — no cross-context foreign key. Unique (account_id, source, external_event_id).
/// </summary>
public class AdChangeEvent
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string ExternalEventId { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? Actor { get; set; }
    public string ActorKind { get; set; } = "";
    public long? EntityId { get; set; }
    public string? EntityExternalRef { get; set; }
    public string ChangeType { get; set; } = "";
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string Source { get; set; } = "";
    public string Origin { get; set; } = "";
    public string? MatchedExecutionId { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
