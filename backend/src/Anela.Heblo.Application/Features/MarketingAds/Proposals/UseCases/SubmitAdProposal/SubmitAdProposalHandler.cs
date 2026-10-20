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

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SubmitAdProposal;

public class SubmitAdProposalHandler : IRequestHandler<SubmitAdProposalRequest, SubmitAdProposalResponse>
{
    private readonly IAdProposalRepository _proposals;
    private readonly IAgentRunRepository _agentRuns;
    private readonly IAdAuditLog _audit;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _evaluator;
    private readonly IAdAutonomyService _autonomy;
    private readonly IAdProposalExecutionEnqueuer _enqueuer;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly IOptions<AdProposalOptions> _options;
    private readonly ILogger<SubmitAdProposalHandler> _logger;

    public SubmitAdProposalHandler(IAdProposalRepository proposals, IAgentRunRepository agentRuns, IAdAuditLog audit,
        IAdLimitsContextBuilder limitsContext, IAdLimitsEvaluator evaluator, IAdAutonomyService autonomy,
        IAdProposalExecutionEnqueuer enqueuer, ICurrentUserService currentUser, TimeProvider time,
        IOptions<AdProposalOptions> options, ILogger<SubmitAdProposalHandler> logger)
    {
        _proposals = proposals;
        _agentRuns = agentRuns;
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

    public async Task<SubmitAdProposalResponse> Handle(SubmitAdProposalRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        options.EnsureValid();

        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new SubmitAdProposalResponse(ErrorCodes.Unauthorized);

        var errors = AdProposalShapeValidator.Validate(request.Platform, request.AccountExternalId, request.ActionType, request.Actions, request.Reasoning);
        if (errors.Count > 0) return new SubmitAdProposalResponse(ErrorCodes.MarketingAdsInvalidProposal, AdProposalErrors.Params(errors));

        var creatorKind = await ResolveCreatorKindAsync(request.AgentRunId, principal, cancellationToken);
        if (creatorKind is null) return new SubmitAdProposalResponse(ErrorCodes.MarketingAdsAgentRunNotFound);

        // Stored in the same canonical form the actions carry, or the evaluator sees them as a different account.
        var account = AdActionFactory.NormalizeAccountId(request.Platform, request.AccountExternalId);
        var actions = request.Actions.Select(a => AdActionFactory.Create(request.Platform, account, request.ActionType, a)).ToList();
        var now = _time.GetUtcNow().UtcDateTime;
        var proposal = AdProposal.Create(new AdProposalDraft(request.Platform.ToString(), account, request.ActionType.ToString(),
            principal, creatorKind.Value, request.AgentRunId, AdActionJson.Serialize(actions), actions.Count, request.Reasoning.Trim()),
            now, options.ProposalTtl);

        var evaluation = _evaluator.Evaluate(await _limitsContext.BuildAsync(proposal, actions, ApprovalChannel.Web, null, cancellationToken));
        if (evaluation.Decision == AdLimitsDecision.Denied)
        {
            return new SubmitAdProposalResponse(ErrorCodes.MarketingAdsLimitsDenied, AdProposalErrors.Reason(evaluation.Reason));
        }

        _proposals.Add(proposal);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.Submitted, principal, creatorKind.Value,
            AdAuditEvents.SubmissionChannel(creatorKind.Value),
            new { actionCount = actions.Count, evaluation = evaluation.Decision, evaluation.Reason }, now));
        var autoApproved = await _autonomy.TryAutoApproveAsync(proposal, actions, cancellationToken);
        await _proposals.SaveChangesAsync(cancellationToken);

        return AdProposalMutationCompletion.Complete<SubmitAdProposalResponse>(proposal, actions, evaluation, autoApproved, _enqueuer, _logger);
    }

    private async Task<AdActorKind?> ResolveCreatorKindAsync(Guid? agentRunId, string principal, CancellationToken cancellationToken)
    {
        if (agentRunId is null) return AdActorKind.Human;
        var run = await _agentRuns.FindAsync(agentRunId.Value, cancellationToken);
        return run is not null && run.Principal == principal && run.Status == AgentRunStatus.Running
            ? AdActorKind.Agent
            : null;
    }
}
