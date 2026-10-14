using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

public sealed record AdOriginMatchingRules(TimeSpan Window);

public sealed record AdOriginMatch(AdChangeOrigin Origin, string? MatchedExecutionId)
{
    public static readonly AdOriginMatch OutOfBand = new(AdChangeOrigin.OutOfBand, null);
}

/// <summary>
/// Spec section 5: a change is Heblo's when it matches an executed action - same entity, same
/// change, within the window, and (when the platform reports an actor) made by Heblo's platform user.
/// The actor gate is the read source's ActorKind: User and PlatformAutomation are always out-of-band.
/// Pure: no I/O, so every rule is unit-tested.
/// </summary>
public sealed class AdChangeOriginMatcher
{
    public AdOriginMatch Match(AdChangeEventCandidate change, IReadOnlyList<AdExecutedAction> executed, AdOriginMatchingRules rules)
    {
        if (change.EntityExternalId is null || executed.Count == 0)
            return AdOriginMatch.OutOfBand;
        if (change.ActorKind is AdChangeActorKind.User or AdChangeActorKind.PlatformAutomation)
            return AdOriginMatch.OutOfBand;

        var entityId = change.EntityExternalId;
        var tokens = AdChangeValueTokens.Extract(change.NewValueJson);
        var best = executed
            .Where(action => (action.ExecutedAt - change.OccurredAt).Duration() <= rules.Window)
            .Where(action => TargetsEntity(action, change.EntityLevel, entityId))
            .Where(action => IsSameChange(action, entityId, tokens))
            .OrderBy(action => (action.ExecutedAt - change.OccurredAt).Duration())
            .ThenBy(action => action.ExecutionId, StringComparer.Ordinal)
            .FirstOrDefault();

        return best is null ? AdOriginMatch.OutOfBand : new AdOriginMatch(AdChangeOrigin.Heblo, best.ExecutionId);
    }

    private static bool TargetsEntity(AdExecutedAction action, AdEntityLevel? level, string entityId) =>
        ((level is null || level == action.TargetLevel)
            && string.Equals(action.TargetExternalId, entityId, StringComparison.Ordinal))
        || MatchesPlatformResource(action, entityId);

    // Platforms name created resources by full path ("customers/1/campaignCriteria/55~66"); a change
    // event may carry the full path or just the trailing id.
    private static bool MatchesPlatformResource(AdExecutedAction action, string entityId) =>
        action.PlatformResourceId is { } resourceId
        && (string.Equals(resourceId, entityId, StringComparison.Ordinal)
            || resourceId.EndsWith("/" + entityId, StringComparison.Ordinal));

    private static bool IsSameChange(AdExecutedAction action, string entityId, IReadOnlyList<string> tokens) =>
        action.Type switch
        {
            AdActionType.PauseAd => tokens.Contains(
                action.IsRevert ? AdActionValues.Enabled : AdActionValues.Paused, StringComparer.OrdinalIgnoreCase),
            AdActionType.AddNegativeKeyword =>
                MatchesPlatformResource(action, entityId)
                || (action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var text)
                    && tokens.Contains(text, StringComparer.OrdinalIgnoreCase)),
            _ => false,
        };
}
