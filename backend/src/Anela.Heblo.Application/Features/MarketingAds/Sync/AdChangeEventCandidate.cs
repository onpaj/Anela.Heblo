using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>A change event ready to be matched and stored: UTC time, JSON-safe values.</summary>
public sealed record AdChangeEventCandidate(
    string ExternalEventId,
    DateTimeOffset OccurredAt,
    string? Actor,
    AdChangeActorKind ActorKind,
    AdEntityLevel? EntityLevel,
    string? EntityExternalId,
    string ChangeType,
    string? OldValueJson,
    string? NewValueJson)
{
    public static AdChangeEventCandidate FromRow(AdChangeEventRow row) => new(
        row.ExternalEventId,
        row.OccurredAt.ToUniversalTime(),
        row.Actor,
        row.ActorKind,
        row.EntityLevel,
        row.EntityExternalId,
        row.ChangeType,
        AdJsonValues.Normalize(row.OldValueJson),
        AdJsonValues.Normalize(row.NewValueJson));
}
