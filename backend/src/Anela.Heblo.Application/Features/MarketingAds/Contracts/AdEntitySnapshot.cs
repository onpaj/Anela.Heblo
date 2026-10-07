namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// One campaign / ad group / keyword / ad as the platform reports it. Ids are platform external ids
/// only — the core maps them to <c>ads.ad_entities.id</c>. Platform-specific fields (keyword text and
/// match type, budgets, bidding strategy) go to <paramref name="Attributes"/>, stored as jsonb.
/// </summary>
public sealed record AdEntitySnapshot(
    AdEntityLevel Level, string ExternalId, AdEntityLevel? ParentLevel, string? ParentExternalId,
    string Name, AdEntityStatus Status, IReadOnlyDictionary<string, string?> Attributes);
