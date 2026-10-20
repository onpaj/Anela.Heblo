namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Event types of the append-only ad audit log (spec 6.5).</summary>
public enum AdAuditEventType
{
    Submitted = 1,
    Revised = 2,
    Approved = 3,
    Rejected = 4,
    Expired = 5,
    ExecutionStarted = 6,
    ActionExecuted = 7,
    ActionFailed = 8,
    Reverted = 9,
    SettingChanged = 10,
    KillSwitchChanged = 11,
}
