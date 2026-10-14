using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;

/// <summary>
/// Spec 6.4. Only an Allowed evaluation is auto-approved. Evaluated with channel System, which never
/// satisfies a second-approver rule and always counts as "not web", so NeedsWebApproval and
/// NeedsSecondApprover stay Pending for a human (spec 6.3: the limits apply independent of who approved).
/// </summary>
public sealed class AdAutonomyService : IAdAutonomyService
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IAdAuditLog _audit;
    private readonly TimeProvider _time;

    public AdAutonomyService(IAdGovernanceRepository governance, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, IAdAuditLog audit, TimeProvider time)
    {
        _governance = governance;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _audit = audit;
        _time = time;
    }

    public async Task<bool> TryAutoApproveAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken)
    {
        var mode = (await _governance.GetAutonomySettingsAsync(cancellationToken))
            .FirstOrDefault(s => s.Platform == proposal.Platform && s.ActionType == proposal.ActionType)?.Mode
            ?? AdAutonomyMode.ProposeOnly;
        if (mode == AdAutonomyMode.ProposeOnly)
        {
            return false;
        }

        var request = await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.System, AdSystemPrincipals.Autonomy, cancellationToken);
        var evaluation = _evaluator.Evaluate(request);
        if (!IsAutoApprovable(mode, evaluation.Decision))
        {
            return false;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        proposal.RecordApproval(AdSystemPrincipals.Autonomy, ApprovalChannel.System, now, isFinal: true);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Approved, AdSystemPrincipals.Autonomy,
            AdActorKind.System, ApprovalChannel.System,
            new { autonomyMode = mode, evaluation = evaluation.Decision, evaluation.Reason }, now));
        return true;
    }

    public static bool IsAutoApprovable(AdAutonomyMode mode, AdLimitsDecision decision) => mode switch
    {
        AdAutonomyMode.AutoWithinLimits => decision == AdLimitsDecision.Allowed,
        // Auto is reserved: spec 6.4 does not define it and spec 6.3 binds the limits independent of who
        // approved, so in C3 it behaves exactly like AutoWithinLimits.
        AdAutonomyMode.Auto => decision == AdLimitsDecision.Allowed,
        _ => false,
    };
}
