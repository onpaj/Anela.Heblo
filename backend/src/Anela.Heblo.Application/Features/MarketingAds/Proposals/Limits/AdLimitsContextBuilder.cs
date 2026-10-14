using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;

public sealed class AdLimitsContextBuilder : IAdLimitsContextBuilder
{
    private static readonly TimeSpan PauseWindow = TimeSpan.FromDays(7);
    private static readonly IReadOnlyDictionary<string, AdGroupOccupancy> NoOccupancy = new Dictionary<string, AdGroupOccupancy>();
    private static readonly AdProposalStatus[] ApprovalInFlight = { AdProposalStatus.Approved, AdProposalStatus.Executing };
    private static readonly AdProposalStatus[] ExecutionInFlight = { AdProposalStatus.Executing };

    private readonly IAdGovernanceRepository _governance;
    private readonly IAdProposalRepository _proposals;
    private readonly IAdsReadModel _readModel;
    private readonly TimeProvider _time;

    public AdLimitsContextBuilder(IAdGovernanceRepository governance, IAdProposalRepository proposals, IAdsReadModel readModel, TimeProvider time)
    {
        _governance = governance;
        _proposals = proposals;
        _readModel = readModel;
        _time = time;
    }

    public Task<AdLimitsRequest> BuildAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, ApprovalChannel channel,
        string? approverPrincipal, CancellationToken cancellationToken) =>
        BuildCoreAsync(proposal, actions, channel, approverPrincipal, ApprovalInFlight, cancellationToken);

    public Task<AdLimitsRequest> BuildForExecutionAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken) =>
        BuildCoreAsync(proposal, actions, ApprovalChannel.System, AdSystemPrincipals.Executor, ExecutionInFlight, cancellationToken);

    private async Task<AdLimitsRequest> BuildCoreAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, ApprovalChannel channel,
        string? approverPrincipal, IReadOnlyCollection<AdProposalStatus> inFlight, CancellationToken cancellationToken)
    {
        var platform = Enum.Parse<AdPlatform>(proposal.Platform);
        var actionType = Enum.Parse<AdActionType>(proposal.ActionType);
        var limits = (await _governance.GetLimitSettingsAsync(proposal.Platform, proposal.ActionType, cancellationToken))
            .ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        var isManaged = await _readModel.IsAccountManagedAsync(platform, proposal.AccountExternalId, cancellationToken);

        var pauses = 0;
        var occupancy = NoOccupancy;
        if (actionType == AdActionType.PauseAd)
        {
            var since = _time.GetUtcNow().UtcDateTime - PauseWindow;
            pauses = await _proposals.CountSucceededActionsSinceAsync(proposal.Platform, proposal.AccountExternalId, proposal.ActionType, since, cancellationToken)
                + await _proposals.CountUnrecordedActionsOfOtherProposalsAsync(proposal.Platform, proposal.AccountExternalId, proposal.ActionType,
                    proposal.Id, inFlight, cancellationToken);
            var adIds = actions.Select(a => a.TargetExternalId).Distinct(StringComparer.Ordinal).ToList();
            occupancy = await _readModel.GetAdGroupOccupancyAsync(platform, proposal.AccountExternalId, adIds, cancellationToken);
        }

        return new AdLimitsRequest(platform, proposal.AccountExternalId, actionType, actions, channel, approverPrincipal,
            proposal.GetCurrentHumanApprovers().ToList(), isManaged, pauses, occupancy, limits);
    }
}
