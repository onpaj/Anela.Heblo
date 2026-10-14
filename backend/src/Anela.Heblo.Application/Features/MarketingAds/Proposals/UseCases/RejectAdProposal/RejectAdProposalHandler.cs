using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RejectAdProposal;

/// <summary>Rejects the proposal's current version with a reason, through the channel resolved server-side.</summary>
public class RejectAdProposalHandler : IRequestHandler<RejectAdProposalRequest, RejectAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<RejectAdProposalHandler> _logger;

    public RejectAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IApprovalChannelResolver channelResolver,
        ICurrentUserService currentUser, TimeProvider time, ILogger<RejectAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<RejectAdProposalResponse> Handle(RejectAdProposalRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new RejectAdProposalResponse(ErrorCodes.Unauthorized);

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || reason.Length > RejectAdProposalRequest.MaxReasonLength)
        {
            return new RejectAdProposalResponse(ErrorCodes.MarketingAdsInvalidProposal,
                AdProposalErrors.Params(new[] { $"Reason is required (max {RejectAdProposalRequest.MaxReasonLength} chars)." }));
        }

        var resolution = _channelResolver.Resolve();
        if (!resolution.Allowed) return new RejectAdProposalResponse(ErrorCodes.MarketingAdsApprovalChannelDenied, AdProposalErrors.Reason(resolution.DenyReason));

        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (AdProposalGuards.CheckPendingVersion(proposal, request.Version, now) is { } guard) return new RejectAdProposalResponse(guard);
        if (AdProposalGuards.CheckNotDecidedBy(proposal!, principal) is { } decided) return new RejectAdProposalResponse(decided);

        proposal!.Reject(principal, resolution.Channel, reason, now);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Rejected, principal, AdActorKind.Human, resolution.Channel,
            new { reason }, now));

        if (!await _proposals.TrySaveChangesAsync(cancellationToken))
        {
            _logger.LogWarning("Concurrent change while rejecting ad proposal {ProposalId}", request.Id);
            return new RejectAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }

        return new RejectAdProposalResponse { ProposalId = proposal.Id, Version = proposal.CurrentVersion, Status = proposal.Status };
    }
}
