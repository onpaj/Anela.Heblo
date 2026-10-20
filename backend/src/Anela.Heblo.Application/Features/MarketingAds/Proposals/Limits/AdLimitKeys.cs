namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Keys of AdLimitSettings rows. Must match MarketingAdsSeedData (pinned by a test).</summary>
public static class AdLimitKeys
{
    public const string Enabled = "Enabled";
    public const string MaxActionsPerProposal = "MaxActionsPerProposal";
    public const string McpMaxActionsPerProposal = "McpMaxActionsPerProposal";
    public const string MaxPausedAdsPer7Days = "MaxPausedAdsPer7Days";
    public const string SecondApproverAbove = "SecondApproverAbove";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Enabled, MaxActionsPerProposal, McpMaxActionsPerProposal, MaxPausedAdsPer7Days, SecondApproverAbove,
    };
}
