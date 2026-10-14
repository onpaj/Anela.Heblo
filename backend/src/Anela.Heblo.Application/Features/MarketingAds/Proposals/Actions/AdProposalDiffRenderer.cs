using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>
/// Human-readable diff rendered by Heblo from the structured actions (spec 6.1). There is deliberately
/// no parameter for the proposer's reasoning: the diff can only say what the actions do.
/// </summary>
public static class AdProposalDiffRenderer
{
    private const int MaxValueLength = 120;

    public static IReadOnlyList<string> Render(IReadOnlyList<AdAction> actions, IReadOnlyDictionary<string, string> entityNames) =>
        actions.Select(a => RenderLine(a, entityNames)).ToList();

    public static string RenderLine(AdAction action, IReadOnlyDictionary<string, string> entityNames) => action.Type switch
    {
        AdActionType.AddNegativeKeyword =>
            $"Přidat vylučující slovo „{Clean(Payload(action, AdActionPayloadKeys.Text))}“ ({MatchLabel(Payload(action, AdActionPayloadKeys.MatchType))}) do {LevelLabel(action.TargetLevel)} {Target(action, entityNames)}",
        AdActionType.PauseAd =>
            $"Pozastavit reklamu {Target(action, entityNames)} (aktivní → pozastavená)",
        _ => $"Neznámá akce {action.Type} na {Target(action, entityNames)}",
    };

    /// <summary>One line, no control/format/separator characters, bounded length.</summary>
    public static string Clean(string value)
    {
        var withoutForbidden = new string(value.Select(c => AdTextSafety.IsForbidden(c) ? ' ' : c).ToArray());
        var singleSpaced = string.Join(' ', withoutForbidden.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return singleSpaced.Length <= MaxValueLength ? singleSpaced : singleSpaced[..MaxValueLength] + "…";
    }

    private static string Target(AdAction action, IReadOnlyDictionary<string, string> entityNames) =>
        entityNames.TryGetValue(action.TargetExternalId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"„{Clean(name)}“ [{Clean(action.TargetExternalId)}]"
            : $"[{Clean(action.TargetExternalId)}]";

    private static string Payload(AdAction action, string key) =>
        action.Payload.TryGetValue(key, out var value) ? value : string.Empty;

    private static string MatchLabel(string matchType) => matchType switch
    {
        nameof(KeywordMatchType.Exact) => "přesná shoda",
        nameof(KeywordMatchType.Phrase) => "frázová shoda",
        nameof(KeywordMatchType.Broad) => "volná shoda",
        _ => Clean(matchType),
    };

    private static string LevelLabel(AdEntityLevel level) => level switch
    {
        AdEntityLevel.Campaign => "kampaně",
        AdEntityLevel.AdGroup => "reklamní sestavy",
        _ => level.ToString(),
    };
}
