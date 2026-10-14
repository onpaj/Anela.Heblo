namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>
/// What happened to one action when its proposal version executed: the before-state read from the
/// platform, the platform response and, after a revert, the revert result. Platform/account/type are
/// denormalised so the out-of-band matcher and the weekly pause limit can query without joins.
/// </summary>
public class AdProposalActionExecution
{
    public long Id { get; set; }
    public int ProposalId { get; set; }
    public int Version { get; set; }
    public int ActionIndex { get; set; }
    public string Platform { get; set; } = null!;
    public string AccountExternalId { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public string TargetLevel { get; set; } = null!;
    public string TargetExternalId { get; set; } = null!;
    /// <summary>The action's payload (keyword text, match type) as JSON — needed by the out-of-band matcher.</summary>
    public string PayloadJson { get; set; } = "{}";
    public AdActionExecutionState State { get; set; }
    public string? BeforeValue { get; set; }
    public string? BeforeRawJson { get; set; }
    public string? AfterValue { get; set; }
    public string? PlatformResourceId { get; set; }
    public string? PlatformResponseJson { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; }
    public AdActionRevertState RevertState { get; set; }
    public DateTime? RevertedAt { get; set; }
    public string? RevertResponseJson { get; set; }
    public string? RevertError { get; set; }

    public void MarkReverted(DateTime now, string? responseJson)
    {
        RevertState = AdActionRevertState.Reverted;
        RevertedAt = now;
        RevertResponseJson = responseJson;
        RevertError = null;
    }

    public void MarkRevertFailed(DateTime now, string error)
    {
        RevertState = AdActionRevertState.RevertFailed;
        RevertedAt = now;
        RevertError = error;
    }
}
