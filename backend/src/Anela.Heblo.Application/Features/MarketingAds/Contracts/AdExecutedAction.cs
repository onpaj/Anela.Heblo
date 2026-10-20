namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// One action Heblo executed successfully on an ad platform (produced by C3's execution log).
/// </summary>
/// <remarks>
/// PlatformResourceId is what the platform created, as the executor reports it (for Google Ads the full
/// resource name, e.g. "customers/1/campaignCriteria/55~66"). The matcher compares it to a change event's
/// entity id by equality or by "/"-suffix, so the bare "55~66" matches too.
/// </remarks>
public sealed record AdExecutedAction(
    string ExecutionId,
    AdActionType Type,
    AdPlatform Platform,
    string AccountExternalId,
    AdEntityLevel TargetLevel,
    string TargetExternalId,
    string? PlatformResourceId,
    DateTimeOffset ExecutedAt,
    IReadOnlyDictionary<string, string> Payload,
    bool IsRevert = false);   // true = Heblo reverted this action at ExecutedAt (C3 emits one row per revert)
