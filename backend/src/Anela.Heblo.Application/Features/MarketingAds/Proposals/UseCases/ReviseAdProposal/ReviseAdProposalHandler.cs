using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ReviseAdProposal;

public class ReviseAdProposalHandler : IRequestHandler<ReviseAdProposalRequest, ReviseAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IAdAutonomyService _autonomy;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly IOptions<AdProposalOptions> _options;
    private readonly ILogger<ReviseAdProposalHandler> _logger;

    public ReviseAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IAdLimitsContextBuilder limitsContext,
        IAdLimitsEvaluator evaluator, IAdAutonomyService autonomy, IAdProposalExecutionEnqueuer enqueuer,
        ICurrentUserService currentUser, TimeProvider time, IOptions<AdProposalOptions> options, ILogger<ReviseAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _limitsContext = limitsContext;
        _evaluator = evaluator;
        _autonomy = autonomy;
        _enqueuer = enqueuer;
        _currentUser = currentUser;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public async Task<ReviseAdProposalResponse> Handle(ReviseAdProposalRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        options.EnsureValid();

        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new ReviseAdProposalResponse(ErrorCodes.Unauthorized);

        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        // Refuses an expired-but-still-Pending proposal too, so a revision can never extend its expiry.
        if (AdProposalGuards.CheckPendingVersion(proposal, request.ExpectedVersion, now) is { } guard) return new ReviseAdProposalResponse(guard);
        if (!string.Equals(proposal!.CreatedByPrincipal, principal, StringComparison.Ordinal)) return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsNotProposalOwner);

        var platform = Enum.Parse<AdPlatform>(proposal.Platform);
        var actionType = Enum.Parse<AdActionType>(proposal.ActionType);
        var errors = AdProposalShapeValidator.Validate(platform, proposal.AccountExternalId, actionType, request.Actions, request.Reasoning);
        if (errors.Count > 0) return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsInvalidProposal, AdProposalErrors.Params(errors));

        // A revision always targets the stored account, exactly as stored.
        var actions = request.Actions
            .Select(a => AdActionFactory.Create(platform, proposal.AccountExternalId, actionType, a) with { AccountExternalId = proposal.AccountExternalId })
            .ToList();
        var fromVersion = proposal.CurrentVersion;
        proposal.Revise(AdActionJson.Serialize(actions), actions.Count, request.Reasoning.Trim(), principal, now, options.ProposalTtl);

        var evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.Web, null, cancellationToken));
        if (evaluation.Decision == AdLimitsDecision.Denied)
        {
            _proposals.DiscardChanges();
            return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsLimitsDenied, AdProposalErrors.Reason(evaluation.Reason));
        }

        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Revised, principal, proposal.CreatedByKind,
            AdAuditEvents.SubmissionChannel(proposal.CreatedByKind),
            new { fromVersion, actionCount = actions.Count, evaluation = evaluation.Decision, evaluation.Reason }, now));
        var autoApproved = await _autonomy.TryAutoApproveAsync(proposal, actions, cancellationToken);

        if (!await _proposals.TrySaveChangesAsync(cancellationToken))
        {
            _logger.LogWarning("Concurrent change while revising ad proposal {ProposalId}", request.Id);
            return new ReviseAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }

        return AdProposalMutationCompletion.Complete<ReviseAdProposalResponse>(proposal, actions, evaluation, autoApproved, _enqueuer, _logger);
    }
}
