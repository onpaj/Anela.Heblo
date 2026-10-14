using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals;

public static class AdProposalDtoMapper
{
    public static AdLimitsEvaluationDto ToDto(AdLimitsEvaluation evaluation) =>
        new() { Decision = evaluation.Decision, Reason = evaluation.Reason };

    public static IReadOnlyCollection<string> TargetIds(AdProposal proposal) => proposal.Versions
        .SelectMany(v => AdActionJson.Deserialize(v.ActionsJson))
        .Select(a => a.TargetExternalId)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    public static AdProposalSummaryDto ToSummary(AdProposal proposal, IReadOnlyDictionary<string, string> names)
    {
        var current = proposal.GetCurrentVersion();
        return new AdProposalSummaryDto
        {
            Id = proposal.Id,
            CurrentVersion = proposal.CurrentVersion,
            Status = proposal.Status,
            Platform = Enum.Parse<AdPlatform>(proposal.Platform),
            AccountExternalId = proposal.AccountExternalId,
            ActionType = Enum.Parse<AdActionType>(proposal.ActionType),
            ActionCount = current.ActionCount,
            CreatedByPrincipal = proposal.CreatedByPrincipal,
            CreatedByKind = proposal.CreatedByKind,
            AgentRunId = proposal.AgentRunId,
            CreatedAt = proposal.CreatedAt,
            ExpiresAt = proposal.ExpiresAt,
            UpdatedAt = proposal.UpdatedAt,
            DiffLines = Render(current, names),
        };
    }

    public static AdProposalDetailDto ToDetail(AdProposal proposal, IReadOnlyDictionary<string, string> names, AdLimitsEvaluation? currentEvaluation) => new()
    {
        Summary = ToSummary(proposal, names),
        Versions = proposal.Versions.OrderByDescending(v => v.Version).Select(v => new AdProposalVersionDto
        {
            Version = v.Version,
            ActionCount = v.ActionCount,
            Reasoning = v.Reasoning,
            CreatedBy = v.CreatedBy,
            CreatedAt = v.CreatedAt,
            DiffLines = Render(v, names),
        }).ToList(),
        Approvals = proposal.Approvals.OrderBy(a => a.At).Select(a => new AdProposalApprovalDto
        {
            Version = a.Version, Principal = a.Principal, Channel = a.Channel, Decision = a.Decision, Reason = a.Reason, At = a.At,
        }).ToList(),
        Executions = proposal.Executions.OrderBy(e => e.Version).ThenBy(e => e.ActionIndex).Select(e => new AdActionExecutionDto
        {
            Version = e.Version, ActionIndex = e.ActionIndex, TargetExternalId = e.TargetExternalId, State = e.State,
            BeforeValue = e.BeforeValue, AfterValue = e.AfterValue, PlatformResourceId = e.PlatformResourceId, Error = e.Error,
            ValidateOnly = e.State == AdActionExecutionState.Failed && AdExecutionErrors.IsValidateOnly(e.Error),
            ExecutedAt = e.ExecutedAt, RevertState = e.RevertState, RevertedAt = e.RevertedAt, RevertError = e.RevertError,
            RevertValidateOnly = e.RevertState == AdActionRevertState.RevertFailed && AdExecutionErrors.IsValidateOnly(e.RevertError),
        }).ToList(),
        CurrentEvaluation = currentEvaluation is null ? null : ToDto(currentEvaluation),
    };

    private static List<string> Render(AdProposalVersion version, IReadOnlyDictionary<string, string> names) =>
        AdProposalDiffRenderer.Render(AdActionJson.Deserialize(version.ActionsJson), names).ToList();
}
