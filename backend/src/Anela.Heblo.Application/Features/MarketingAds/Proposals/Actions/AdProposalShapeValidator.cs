using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>
/// Structural validation of a submitted/revised proposal (spec 6.1: 1–50 actions, single type and account).
/// Per-type and per-channel caps are the limits engine's job, not this validator's.
/// </summary>
public static class AdProposalShapeValidator
{
    public const int MaxActionsPerProposal = 50;
    public const int MaxExternalIdLength = 64;
    public const int MaxKeywordLength = 80;
    public const int MaxReasoningLength = 10_000;

    public static IReadOnlyList<string> Validate(AdPlatform platform, string? accountExternalId, AdActionType actionType,
        IReadOnlyList<AdActionInputDto>? actions, string? reasoning)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(platform)) errors.Add("Unknown platform.");
        if (!Enum.IsDefined(actionType)) errors.Add("Unknown action type.");
        if (!IsValidId(accountExternalId)) errors.Add($"AccountExternalId is required (max {MaxExternalIdLength} chars, no control characters).");
        if (string.IsNullOrWhiteSpace(reasoning)) errors.Add("Reasoning is required.");
        else if (reasoning.Length > MaxReasoningLength) errors.Add($"Reasoning is longer than {MaxReasoningLength} characters.");

        if (actions is null || actions.Count == 0) errors.Add("At least one action is required.");
        else if (actions.Count > MaxActionsPerProposal) errors.Add($"A proposal holds at most {MaxActionsPerProposal} actions.");
        else errors.AddRange(ValidateActions(actionType, actions));
        return errors;
    }

    private static IEnumerable<string> ValidateActions(AdActionType actionType, IReadOnlyList<AdActionInputDto> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            foreach (var error in ValidateAction(actionType, actions[i]))
            {
                yield return $"Action {i + 1}: {error}";
            }
        }

        var duplicates = actions.Where(a => a is not null)
            .GroupBy(a => DedupKey(actionType, a))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var key in duplicates)
        {
            yield return $"Duplicate action: {key}.";
        }
    }

    private static IEnumerable<string> ValidateAction(AdActionType actionType, AdActionInputDto? action)
    {
        if (action is null)
        {
            yield return "is empty.";
            yield break;
        }
        if (!IsValidId(action.TargetExternalId)) yield return $"TargetExternalId is required (max {MaxExternalIdLength} chars, no control characters).";
        if (!Enum.IsDefined(action.TargetLevel)) yield return "Unknown TargetLevel.";

        switch (actionType)
        {
            case AdActionType.AddNegativeKeyword:
                if (action.TargetLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup)) yield return "TargetLevel must be Campaign or AdGroup.";
                if (!IsValidText(action.KeywordText, MaxKeywordLength)) yield return $"KeywordText is required (max {MaxKeywordLength} chars, no control characters).";
                if (action.MatchType is null || !Enum.IsDefined(action.MatchType.Value)) yield return "MatchType is required.";
                break;
            case AdActionType.PauseAd:
                if (action.TargetLevel != AdEntityLevel.Ad) yield return "TargetLevel must be Ad.";
                if (action.KeywordText is not null || action.MatchType is not null) yield return "PauseAd takes no keyword.";
                break;
        }
    }

    private static string DedupKey(AdActionType actionType, AdActionInputDto action) =>
        actionType == AdActionType.AddNegativeKeyword
            ? $"{action.TargetLevel}:{action.TargetExternalId?.Trim()}:{action.KeywordText?.Trim().ToLowerInvariant()}:{action.MatchType}"
            : $"{action.TargetExternalId?.Trim()}";

    private static bool IsValidId(string? value) => IsValidText(value, MaxExternalIdLength);

    private static bool IsValidText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength && !AdTextSafety.ContainsForbidden(value);
}
