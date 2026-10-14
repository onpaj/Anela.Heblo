using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;

public class FinishAgentRunHandler : IRequestHandler<FinishAgentRunRequest, FinishAgentRunResponse>
{
    private readonly IAgentRunRepository _agentRuns;
    private readonly IAdProposalRepository _proposals;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public FinishAgentRunHandler(IAgentRunRepository agentRuns, IAdProposalRepository proposals, ICurrentUserService currentUser, TimeProvider time)
    {
        _agentRuns = agentRuns;
        _proposals = proposals;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<FinishAgentRunResponse> Handle(FinishAgentRunRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new FinishAgentRunResponse(ErrorCodes.Unauthorized);

        // Another principal's run is reported as missing so its existence is not revealed.
        var run = await _agentRuns.FindAsync(request.AgentRunId, cancellationToken);
        if (run is null || run.Principal != principal) return new FinishAgentRunResponse(ErrorCodes.MarketingAdsAgentRunNotFound);
        if (!IsValid(request) || run.Status != AgentRunStatus.Running) return new FinishAgentRunResponse(ErrorCodes.MarketingAdsInvalidAgentRun);

        var proposalsCreated = await _proposals.CountProposalsForRunAsync(run.Id, cancellationToken);
        run.Finish(request.Status, request.Summary?.Trim(), JsonSerializer.Serialize(request.DataScopesRead ?? new()), proposalsCreated,
            request.InputTokens, request.OutputTokens, request.CostCzk, _time.GetUtcNow().UtcDateTime);
        await _agentRuns.SaveChangesAsync(cancellationToken);
        return new FinishAgentRunResponse { Run = AdProposalDtoMapper.ToDto(run) };
    }

    private static bool IsValid(FinishAgentRunRequest r) =>
        r.Status is AgentRunStatus.Succeeded or AgentRunStatus.Failed
        && (r.Summary is null || r.Summary.Length <= FinishAgentRunRequest.MaxSummaryLength)
        && AgentRunScopes.AreValid(r.DataScopesRead)
        && (r.InputTokens is null or >= 0)
        && (r.OutputTokens is null or >= 0)
        && (r.CostCzk is null or >= 0);
}
