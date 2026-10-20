using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Persistence.MarketingAds;

/// <summary>
/// Seeds of the governance tables (spec 6.3, 6.4, 6.6). Strings are the C1 enum names
/// (AdPlatform, AdActionType) and the Application AdLimitKeys — a unit test pins both.
/// SecondApproverAbove is seeded equal to the per-proposal maximum, i.e. inactive until an admin lowers it.
/// </summary>
public static class MarketingAdsSeedData
{
    public const string SeededBy = "seed";
    public static readonly DateTime SeededAt = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    public static readonly string[] Platforms = { "GoogleAds", "MetaAds", "Sklik" };
    public static readonly string[] ActionTypes = { "AddNegativeKeyword", "PauseAd" };

    public static AdAutonomySetting[] Autonomy() =>
        (from platform in Platforms
         from actionType in ActionTypes
         select new AdAutonomySetting
         {
             Platform = platform,
             ActionType = actionType,
             Mode = AdAutonomyMode.ProposeOnly,
             UpdatedAt = SeededAt,
             UpdatedBy = SeededBy,
         }).ToArray();

    public static AdLimitSetting[] Limits() => Platforms.SelectMany(LimitsFor).ToArray();

    public static AdKillSwitch[] KillSwitches() => new[]
    {
        new AdKillSwitch { Scope = AdKillSwitch.GlobalScope, ExecutionEnabled = false, UpdatedAt = SeededAt, UpdatedBy = SeededBy },
    };

    private static IEnumerable<AdLimitSetting> LimitsFor(string platform)
    {
        // Meta has no negative keywords (spec 4.3).
        yield return Limit(platform, "AddNegativeKeyword", "Enabled", platform == "MetaAds" ? 0 : 1);
        yield return Limit(platform, "AddNegativeKeyword", "MaxActionsPerProposal", 50);
        yield return Limit(platform, "AddNegativeKeyword", "McpMaxActionsPerProposal", 20);
        yield return Limit(platform, "AddNegativeKeyword", "SecondApproverAbove", 50);
        yield return Limit(platform, "PauseAd", "Enabled", 1);
        yield return Limit(platform, "PauseAd", "MaxActionsPerProposal", 10);
        yield return Limit(platform, "PauseAd", "McpMaxActionsPerProposal", 3);
        yield return Limit(platform, "PauseAd", "MaxPausedAdsPer7Days", 20);
        yield return Limit(platform, "PauseAd", "SecondApproverAbove", 10);
    }

    private static AdLimitSetting Limit(string platform, string actionType, string key, int value) => new()
    {
        Platform = platform,
        ActionType = actionType,
        Key = key,
        Value = value,
        UpdatedAt = SeededAt,
        UpdatedBy = SeededBy,
    };
}
