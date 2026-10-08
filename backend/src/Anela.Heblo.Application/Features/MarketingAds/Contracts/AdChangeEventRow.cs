namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>One entry of a platform's change history. Old/new values are JSON (stored as jsonb) or null.</summary>
public sealed record AdChangeEventRow(
    string ExternalEventId, DateTimeOffset OccurredAt, string? Actor, AdChangeActorKind ActorKind,
    AdEntityLevel? EntityLevel, string? EntityExternalId, string ChangeType,
    string? OldValueJson, string? NewValueJson);
