using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ApproveAdProposal;

/// <summary>
/// Records a human approval of the proposal's current version through the channel resolved server-side.
/// Exactly one concurrent write wins (Revision token); the loser gets MarketingAdsConcurrentChange.
/// The Executor is checked by the execution job, not here.
/// </summary>
public class ApproveAdProposalHandler : IRequestHandler<ApproveAdProposalRequest, ApproveAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<ApproveAdProposalHandler> _logger;

    public ApproveAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, IApprovalChannelResolver channelResolver, IAdProposalExecutionEnqueuer enqueuer,
        ICurrentUserService currentUser, TimeProvider time, ILogger<ApproveAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _channelResolver = channelResolver;
        _enqueuer = enqueuer;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<ApproveAdProposalResponse> Handle(ApproveAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new ApproveAdProposalResponse(ErrorCodes.Unauthorized);

        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed) return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(resolution.DenyReason));

        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (AdProposalGuards.CheckPendingVersion(proposal, request.Version, now) is { } guard) return new ApproveAdProposalResponse(guard);
        if (AdProposalGuards.CheckNotDecidedBy(proposal!, principal) is { } decided) return new ApproveAdProposalResponse(decided);

        var actions = AdActionJson.Deserialize(proposal!.GetCurrentVersion().ActionsJson);
        var evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, resolution.Channel, principal, cancellationToken));
        switch (evaluation.Decision)
        {
            case AdLimitsDecision.Denied:
                return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsLimitsDenied, AdProposalErrors.Reason(evaluation.Reason));
            case AdLimitsDecision.NeedsWebApproval:
                return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsNeedsWebApproval, AdProposalErrors.Reason(evaluation.Reason));
        }

        var isFinal = evaluation.Decision == AdLimitsDecision.Allowed;
        proposal.RecordApproval(principal, resolution.Channel, now, isFinal);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Approved, principal, AdActorKind.Human, resolution.Channel,
            new { evaluation = evaluation.Decision, evaluation.Reason, awaitingSecondApprover = !isFinal }, now));

        if (!await _proposals.TrySaveChangesAsync(cancellationToken))
        {
            _logger.LogWarning("Concurrent change while approving ad proposal {ProposalId}", request.Id);
            return new ApproveAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }

        var jobId = isFinal ? _enqueuer.EnqueueExecution(proposal.Id) : null;
        if (isFinal && jobId is null)
        {
            _logger.LogWarning("Ad proposal {ProposalId} approved but enqueueing failed; the expiry job re-enqueues it.", proposal.Id);
        }

        return new ApproveAdProposalResponse
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            Status = proposal.Status,
            AwaitingSecondApprover = !isFinal,
            ExecutionJobId = jobId,
        };
    }
}
