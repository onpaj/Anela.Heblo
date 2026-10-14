using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;

public class AdLimitsEvaluationDto
{
    public AdLimitsDecision Decision { get; set; }
    public string? Reason { get; set; }
}

public class AdProposalSummaryDto
{
    public int Id { get; set; }
    public int CurrentVersion { get; set; }
    public AdProposalStatus Status { get; set; }
    public AdPlatform Platform { get; set; }
    public string AccountExternalId { get; set; } = null!;
    public AdActionType ActionType { get; set; }
    public int ActionCount { get; set; }
    public string CreatedByPrincipal { get; set; } = null!;
    public AdActorKind CreatedByKind { get; set; }
    public Guid? AgentRunId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Rendered by Heblo from the current version's actions.</summary>
    public List<string> DiffLines { get; set; } = new();
}

public class AdProposalVersionDto
{
    public int Version { get; set; }
    public int ActionCount { get; set; }
    /// <summary>The proposer's free text. Display separately from the diff, as untrusted text.</summary>
    public string Reasoning { get; set; } = null!;
    public string CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public List<string> DiffLines { get; set; } = new();
}

public class AdProposalApprovalDto
{
    public int Version { get; set; }
    public string Principal { get; set; } = null!;
    public ApprovalChannel Channel { get; set; }
    public ApprovalDecision Decision { get; set; }
    public string? Reason { get; set; }
    public DateTime At { get; set; }
}

public class AdActionExecutionDto
{
    public int Version { get; set; }
    public int ActionIndex { get; set; }
    public string TargetExternalId { get; set; } = null!;
    public AdActionExecutionState State { get; set; }
    public string? BeforeValue { get; set; }
    public string? AfterValue { get; set; }
    public string? PlatformResourceId { get; set; }
    public string? Error { get; set; }
    public DateTime ExecutedAt { get; set; }
    public AdActionRevertState RevertState { get; set; }
    public DateTime? RevertedAt { get; set; }
    public string? RevertError { get; set; }
}

public class AdProposalDetailDto
{
    public AdProposalSummaryDto Summary { get; set; } = new();
    public List<AdProposalVersionDto> Versions { get; set; } = new();
    public List<AdProposalApprovalDto> Approvals { get; set; } = new();
    public List<AdActionExecutionDto> Executions { get; set; } = new();
    /// <summary>Limits evaluation for the current user on the Web channel; null unless Pending.</summary>
    public AdLimitsEvaluationDto? CurrentEvaluation { get; set; }
}
