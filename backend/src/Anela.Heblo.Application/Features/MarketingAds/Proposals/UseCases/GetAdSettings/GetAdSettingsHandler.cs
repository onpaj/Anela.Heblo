using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Domain.Features.MarketingAds;
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;

/// <summary>Guardrails as data: also what C4's GetAdGuardrails MCP tool returns to agents.</summary>
public class GetAdSettingsHandler : IRequestHandler<GetAdSettingsRequest, GetAdSettingsResponse>
{
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdProposalRepository _proposals;

    public GetAdSettingsHandler(IAdGovernanceRepository governance, IAdProposalRepository proposals)
    {
        _governance = governance;
        _proposals = proposals;
    }

    public async Task<GetAdSettingsResponse> Handle(GetAdSettingsRequest request, CancellationToken cancellationToken)
    {
        var autonomy = await _governance.GetAutonomySettingsAsync(cancellationToken);
        var limits = await _governance.GetLimitSettingsAsync(null, null, cancellationToken);
        var switches = await _governance.GetKillSwitchesAsync(cancellationToken);
        var outcomes = await _proposals.GetApprovalOutcomeCountsAsync(cancellationToken);

        return new GetAdSettingsResponse
        {
            Autonomy = autonomy.Where(a => IsKnown(a.Platform, a.ActionType)).Select(a => new AdAutonomySettingDto
            {
                Platform = Enum.Parse<AdPlatform>(a.Platform),
                ActionType = Enum.Parse<AdActionType>(a.ActionType),
                Mode = a.Mode,
                UpdatedAt = a.UpdatedAt,
                UpdatedBy = a.UpdatedBy,
            }).ToList(),
            Limits = limits.Where(l => IsKnown(l.Platform, l.ActionType)).Select(l => new AdLimitSettingDto
            {
                Platform = Enum.Parse<AdPlatform>(l.Platform),
                ActionType = Enum.Parse<AdActionType>(l.ActionType),
                Key = l.Key,
                Value = l.Value,
                UpdatedAt = l.UpdatedAt,
                UpdatedBy = l.UpdatedBy,
            }).ToList(),
            KillSwitches = switches.Select(s => new AdKillSwitchDto
            {
                Scope = s.Scope,
                ExecutionEnabled = s.ExecutionEnabled,
                UpdatedAt = s.UpdatedAt,
                UpdatedBy = s.UpdatedBy,
            }).ToList(),
            IsExecutionEnabledGlobally = switches.Any(s => s.Scope == AdKillSwitch.GlobalScope && s.ExecutionEnabled),
            ApprovalRates = outcomes.Where(o => IsKnown(o.Platform, o.ActionType)).Select(o => new AdApprovalRateDto
            {
                Platform = Enum.Parse<AdPlatform>(o.Platform),
                ActionType = Enum.Parse<AdActionType>(o.ActionType),
                Decided = o.Decided,
                ApprovedWithoutEdit = o.ApprovedWithoutEdit,
                Rate = o.Decided == 0 ? null : Math.Round((decimal)o.ApprovedWithoutEdit / o.Decided, 4),
            }).ToList(),
        };
    }

    private static bool IsKnown(string platform, string actionType) =>
        Enum.TryParse<AdPlatform>(platform, out _) && Enum.TryParse<AdActionType>(actionType, out _);
}
