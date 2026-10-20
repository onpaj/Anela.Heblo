namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public sealed record AdLimitsEvaluation(AdLimitsDecision Decision, string? Reason)
{
    public static AdLimitsEvaluation Allow() => new(AdLimitsDecision.Allowed, null);
    public static AdLimitsEvaluation Deny(string reason) => new(AdLimitsDecision.Denied, reason);
    public static AdLimitsEvaluation RequireWebApproval(string reason) => new(AdLimitsDecision.NeedsWebApproval, reason);
    public static AdLimitsEvaluation RequireSecondApprover(string reason) => new(AdLimitsDecision.NeedsSecondApprover, reason);
}
