using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;

public class StartAgentRunHandler : IRequestHandler<StartAgentRunRequest, StartAgentRunResponse>
{
    private readonly IAgentRunRepository _agentRuns;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public StartAgentRunHandler(IAgentRunRepository agentRuns, ICurrentUserService currentUser, TimeProvider time)
    {
        _agentRuns = agentRuns;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<StartAgentRunResponse> Handle(StartAgentRunRequest request, CancellationToken cancellationToken)
    {
        var principal = _currentUser.GetCurrentUser().Id;
        if (string.IsNullOrWhiteSpace(principal)) return new StartAgentRunResponse(ErrorCodes.Unauthorized);
        var name = request.AgentName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > StartAgentRunRequest.MaxAgentNameLength || AdTextSafety.ContainsNul(name)
            || !AgentRunScopes.AreValid(request.DataScopesRead))
        {
            return new StartAgentRunResponse(ErrorCodes.MarketingAdsInvalidAgentRun);
        }

        var run = AgentRun.Start(Guid.NewGuid(), name, principal, JsonSerializer.Serialize(request.DataScopesRead ?? new()), _time.GetUtcNow().UtcDateTime);
        _agentRuns.Add(run);
        await _agentRuns.SaveChangesAsync(cancellationToken);
        return new StartAgentRunResponse { AgentRunId = run.Id };
    }
}
