using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public sealed class AdLimitsEvaluator : IAdLimitsEvaluator
{
    public AdLimitsEvaluation Evaluate(AdLimitsRequest request)
    {
        var denial = FindDenial(request);
        if (denial is not null)
        {
            return AdLimitsEvaluation.Deny(denial);
        }

        var webOnly = FindWebOnlyReason(request);
        if (webOnly is not null)
        {
            return AdLimitsEvaluation.RequireWebApproval(webOnly);
        }

        return NeedsSecondApprover(request)
            ? AdLimitsEvaluation.RequireSecondApprover(AdLimitReasons.SecondApproverRequired)
            : AdLimitsEvaluation.Allow();
    }

    private static string? FindDenial(AdLimitsRequest r)
    {
        if (Limit(r, AdLimitKeys.Enabled) != 1) return AdLimitReasons.ActionTypeNotEnabled;
        if (r.IsAccountManaged != true) return AdLimitReasons.AccountNotManaged;
        if (r.Actions.Count == 0) return AdLimitReasons.NoActions;
        if (r.Actions.Any(a => a.Type != r.ActionType || a.Platform != r.Platform
                               || !string.Equals(a.AccountExternalId, r.AccountExternalId, StringComparison.Ordinal)))
        {
            return AdLimitReasons.MixedActions;
        }
        if (r.Actions.Count > (Limit(r, AdLimitKeys.MaxActionsPerProposal) ?? 0)) return AdLimitReasons.TooManyActions;
        if (r.ActionType == AdActionType.PauseAd
            && r.SucceededPausesLast7Days + r.Actions.Count > (Limit(r, AdLimitKeys.MaxPausedAdsPer7Days) ?? 0))
        {
            return AdLimitReasons.WeeklyPauseLimit;
        }
        return null;
    }

    private static string? FindWebOnlyReason(AdLimitsRequest r)
    {
        if (r.Channel == ApprovalChannel.Web) return null;
        if (r.Channel == ApprovalChannel.Mcp && r.Actions.Count > (Limit(r, AdLimitKeys.McpMaxActionsPerProposal) ?? 0))
        {
            return AdLimitReasons.McpThreshold;
        }
        if (r.ActionType == AdActionType.PauseAd && WouldEmptyAnAdGroup(r)) return AdLimitReasons.AdGroupWouldBeEmpty;
        return null;
    }

    private static bool WouldEmptyAnAdGroup(AdLimitsRequest r)
    {
        var pausedPerGroup = new Dictionary<string, (int Paused, int Enabled)>(StringComparer.Ordinal);
        foreach (var action in r.Actions)
        {
            // An ad the read model does not know cannot be proven safe.
            if (!r.AdGroupOccupancy.TryGetValue(action.TargetExternalId, out var occupancy)) return true;
            var current = pausedPerGroup.GetValueOrDefault(occupancy.AdGroupExternalId, (Paused: 0, Enabled: occupancy.EnabledAdCount));
            pausedPerGroup[occupancy.AdGroupExternalId] = (current.Paused + 1, occupancy.EnabledAdCount);
        }
        return pausedPerGroup.Values.Any(g => g.Enabled - g.Paused <= 0);
    }

    private static bool NeedsSecondApprover(AdLimitsRequest r)
    {
        var threshold = Limit(r, AdLimitKeys.SecondApproverAbove);
        if (threshold is null || r.Actions.Count <= threshold) return false;
        if (r.Channel == ApprovalChannel.System || r.ApproverPrincipal is null) return true;
        return !r.PriorApproverPrincipals.Any(p => !string.Equals(p, r.ApproverPrincipal, StringComparison.Ordinal));
    }

    private static int? Limit(AdLimitsRequest r, string key) => r.Limits.TryGetValue(key, out var value) ? value : null;
}
