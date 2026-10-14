using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Limits;

public class AdLimitsEvaluatorTests
{
    private const string Account = "acc-1";

    private static readonly Dictionary<string, int> PauseLimits = new()
    {
        [AdLimitKeys.Enabled] = 1, [AdLimitKeys.MaxActionsPerProposal] = 10, [AdLimitKeys.McpMaxActionsPerProposal] = 3,
        [AdLimitKeys.MaxPausedAdsPer7Days] = 20, [AdLimitKeys.SecondApproverAbove] = 10,
    };

    private static readonly Dictionary<string, int> NegativeLimits = new()
    {
        [AdLimitKeys.Enabled] = 1, [AdLimitKeys.MaxActionsPerProposal] = 50, [AdLimitKeys.McpMaxActionsPerProposal] = 20,
        [AdLimitKeys.SecondApproverAbove] = 50,
    };

    private static AdAction Pause(string adId, string account = Account) => new(AdActionType.PauseAd, AdPlatform.GoogleAds, account,
        AdEntityLevel.Ad, adId, AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());

    private static AdAction Negative(int i) => new(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, Account,
        AdEntityLevel.Campaign, "cmp-1", AdActionValues.Absent, AdActionValues.Present,
        new Dictionary<string, string> { [AdActionPayloadKeys.Text] = $"kw{i}", [AdActionPayloadKeys.MatchType] = "Exact" });

    /// <summary>Each ad sits in its own group with 3 enabled ads unless the case overrides occupancy.</summary>
    private static AdLimitsRequest PauseRequest(int count = 1, ApprovalChannel channel = ApprovalChannel.Web,
        string? approver = "chief", string[]? prior = null, bool? managed = true, int pauses = 0,
        Dictionary<string, AdGroupOccupancy>? occupancy = null, Dictionary<string, int>? limits = null)
    {
        var actions = Enumerable.Range(1, count).Select(i => Pause($"ad-{i}")).ToList();
        occupancy ??= actions.ToDictionary(a => a.TargetExternalId, a => new AdGroupOccupancy($"grp-{a.TargetExternalId}", 3));
        return new AdLimitsRequest(AdPlatform.GoogleAds, Account, AdActionType.PauseAd, actions, channel, approver,
            prior ?? Array.Empty<string>(), managed, pauses, occupancy, limits ?? PauseLimits);
    }

    private static AdLimitsRequest NegativeRequest(int count, ApprovalChannel channel = ApprovalChannel.Web, int pauses = 0) =>
        new(AdPlatform.GoogleAds, Account, AdActionType.AddNegativeKeyword, Enumerable.Range(1, count).Select(Negative).ToList(),
            channel, "chief", Array.Empty<string>(), true, pauses, new Dictionary<string, AdGroupOccupancy>(), NegativeLimits);

    private static Dictionary<string, int> With(Dictionary<string, int> source, string key, int? value)
    {
        var copy = new Dictionary<string, int>(source);
        if (value is null) copy.Remove(key); else copy[key] = value.Value;
        return copy;
    }

    public static TheoryData<string, AdLimitsRequest, AdLimitsDecision, string?> Cases() => new()
    {
        { "baseline web pause", PauseRequest(), AdLimitsDecision.Allowed, null },
        { "type disabled for platform", PauseRequest(limits: With(PauseLimits, AdLimitKeys.Enabled, 0)), AdLimitsDecision.Denied, AdLimitReasons.ActionTypeNotEnabled },
        { "Enabled row missing fails closed", PauseRequest(limits: With(PauseLimits, AdLimitKeys.Enabled, null)), AdLimitsDecision.Denied, AdLimitReasons.ActionTypeNotEnabled },
        { "unmanaged account", PauseRequest(managed: false), AdLimitsDecision.Denied, AdLimitReasons.AccountNotManaged },
        { "unknown account (ads DB not configured)", PauseRequest(managed: null), AdLimitsDecision.Denied, AdLimitReasons.AccountNotManaged },
        { "mixed account", PauseRequest() with { Actions = new[] { Pause("ad-1"), Pause("ad-2", "other") } }, AdLimitsDecision.Denied, AdLimitReasons.MixedActions },
        { "no actions", PauseRequest() with { Actions = Array.Empty<AdAction>() }, AdLimitsDecision.Denied, AdLimitReasons.NoActions },
        { "10 pauses is the max", PauseRequest(10), AdLimitsDecision.Allowed, null },
        { "11 pauses over max", PauseRequest(11), AdLimitsDecision.Denied, AdLimitReasons.TooManyActions },
        { "50 negatives is the max", NegativeRequest(50), AdLimitsDecision.Allowed, null },
        { "51 negatives over max", NegativeRequest(51), AdLimitsDecision.Denied, AdLimitReasons.TooManyActions },
        { "weekly cap reached exactly", PauseRequest(1, pauses: 19), AdLimitsDecision.Allowed, null },
        { "weekly cap exceeded", PauseRequest(1, pauses: 20), AdLimitsDecision.Denied, AdLimitReasons.WeeklyPauseLimit },
        { "weekly cap ignores negatives", NegativeRequest(5, pauses: 500), AdLimitsDecision.Allowed, null },
        { "mcp 3 pauses allowed", PauseRequest(3, ApprovalChannel.Mcp), AdLimitsDecision.Allowed, null },
        { "mcp 4 pauses needs web", PauseRequest(4, ApprovalChannel.Mcp), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.McpThreshold },
        { "web 4 pauses allowed", PauseRequest(4), AdLimitsDecision.Allowed, null },
        { "mcp 20 negatives allowed", NegativeRequest(20, ApprovalChannel.Mcp), AdLimitsDecision.Allowed, null },
        { "mcp 21 negatives needs web", NegativeRequest(21, ApprovalChannel.Mcp), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.McpThreshold },
        { "mcp pause empties group", PauseRequest(1, ApprovalChannel.Mcp, occupancy: new() { ["ad-1"] = new("grp-1", 1) }), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "web pause may empty group", PauseRequest(1, occupancy: new() { ["ad-1"] = new("grp-1", 1) }), AdLimitsDecision.Allowed, null },
        { "mcp two pauses empty a 2-ad group", PauseRequest(2, ApprovalChannel.Mcp, occupancy: new() { ["ad-1"] = new("grp-1", 2), ["ad-2"] = new("grp-1", 2) }), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "mcp unknown ad", PauseRequest(1, ApprovalChannel.Mcp, occupancy: new()), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "system pause empties group", PauseRequest(1, ApprovalChannel.System, approver: "system:autonomy", occupancy: new() { ["ad-1"] = new("grp-1", 1) }), AdLimitsDecision.NeedsWebApproval, AdLimitReasons.AdGroupWouldBeEmpty },
        { "over second-approver threshold", PauseRequest(3, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "same principal again is not a second approver", PauseRequest(3, prior: new[] { "chief" }, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "different prior principal completes", PauseRequest(3, prior: new[] { "specialist" }, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.Allowed, null },
        { "system never satisfies second approver", PauseRequest(3, ApprovalChannel.System, approver: "system:autonomy", limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "no approver yet (submit preview)", PauseRequest(3, approver: null, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, 2)), AdLimitsDecision.NeedsSecondApprover, AdLimitReasons.SecondApproverRequired },
        { "missing second-approver row is optional", PauseRequest(3, limits: With(PauseLimits, AdLimitKeys.SecondApproverAbove, null)), AdLimitsDecision.Allowed, null },
        { "denied wins over needs-web", PauseRequest(4, ApprovalChannel.Mcp, managed: false), AdLimitsDecision.Denied, AdLimitReasons.AccountNotManaged },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Evaluate_AppliesSpecSection63(string because, AdLimitsRequest request, AdLimitsDecision expected, string? reason)
    {
        var result = new AdLimitsEvaluator().Evaluate(request);

        result.Decision.Should().Be(expected, because);
        result.Reason.Should().Be(reason, because);
    }
}
