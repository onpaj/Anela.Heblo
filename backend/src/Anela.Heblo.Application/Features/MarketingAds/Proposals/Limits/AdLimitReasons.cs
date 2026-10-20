namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

/// <summary>Machine-readable reasons returned with an evaluation (shown to agents and in the UI).</summary>
public static class AdLimitReasons
{
    public const string ActionTypeNotEnabled = "ActionTypeNotEnabled";
    public const string AccountNotManaged = "AccountNotManaged";
    public const string NoActions = "NoActions";
    public const string MixedActions = "MixedActions";
    public const string TooManyActions = "TooManyActions";
    public const string WeeklyPauseLimit = "WeeklyPauseLimit";
    public const string McpThreshold = "McpThreshold";
    public const string AdGroupWouldBeEmpty = "AdGroupWouldBeEmpty";
    public const string SecondApproverRequired = "SecondApproverRequired";
}
