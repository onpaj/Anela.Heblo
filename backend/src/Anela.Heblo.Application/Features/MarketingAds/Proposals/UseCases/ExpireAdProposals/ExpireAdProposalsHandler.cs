using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ExpireAdProposals;

/// <summary>
/// Expires Pending proposals past their TTL and re-enqueues Approved ones whose execution never started.
/// Each proposal is saved on its own, so a concurrent change to one does not abort the rest. Re-enqueueing is
/// safe to repeat: the execution job claims a proposal before running it, so a duplicate job is a no-op.
/// </summary>
public class ExpireAdProposalsHandler : IRequestHandler<ExpireAdProposalsRequest, ExpireAdProposalsResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly TimeProvider _time;
    private readonly IOptions<AdProposalOptions> _options;
    private readonly ILogger<ExpireAdProposalsHandler> _logger;

    public ExpireAdProposalsHandler(IAdProposalRepository proposals, IAdAuditLog audit, IAdProposalExecutionEnqueuer enqueuer,
        TimeProvider time, IOptions<AdProposalOptions> options, ILogger<ExpireAdProposalsHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _enqueuer = enqueuer;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public async Task<ExpireAdProposalsResponse> Handle(ExpireAdProposalsRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        options.EnsureValid();

        var now = _time.GetUtcNow().UtcDateTime;
        var expired = 0;
        foreach (var id in await _proposals.GetPendingExpiredIdsAsync(now, cancellationToken))
        {
            if (await TryExpireAsync(id, now, cancellationToken)) expired++;
        }

        var requeued = 0;
        var stuckBefore = now.AddMinutes(-options.StuckApprovedRequeueMinutes);
        foreach (var id in await _proposals.GetStuckApprovedIdsAsync(stuckBefore, cancellationToken))
        {
            if (_enqueuer.EnqueueExecution(id) is not null) requeued++;
        }

        _logger.LogInformation("Ad proposal expiry: {Expired} expired, {Requeued} stuck approvals re-enqueued", expired, requeued);
        return new ExpireAdProposalsResponse { ExpiredCount = expired, RequeuedCount = requeued };
    }

    private async Task<bool> TryExpireAsync(int id, DateTime now, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetForUpdateAsync(id, cancellationToken);
        if (proposal is null || !proposal.IsExpiredAt(now)) return false;

        proposal.Expire(now);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Expired, AdSystemPrincipals.Expiry, AdActorKind.System,
            ApprovalChannel.System, new { proposal.ExpiresAt }, now));

        if (await _proposals.TrySaveChangesAsync(cancellationToken)) return true;

        _logger.LogWarning("Ad proposal {ProposalId} changed while expiring; skipped", id);
        return false;
    }
}
