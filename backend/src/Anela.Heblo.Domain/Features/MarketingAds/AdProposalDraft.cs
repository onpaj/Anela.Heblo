namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>Everything needed to create a proposal. Platform/ActionType are the C1 enum names.</summary>
public sealed record AdProposalDraft(
    string Platform,
    string AccountExternalId,
    string ActionType,
    string CreatedByPrincipal,
    AdActorKind CreatedByKind,
    Guid? AgentRunId,
    string ActionsJson,
    int ActionCount,
    string Reasoning);
