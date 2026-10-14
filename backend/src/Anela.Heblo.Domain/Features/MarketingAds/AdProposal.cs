using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingAds;

/// <summary>
/// A structured, versioned request to change a live ad account (spec 6.1). Rows are never deleted.
/// Platform and ActionType hold the C1 enum names (AdPlatform / AdActionType) because the Domain
/// cannot reference the Application contracts.
/// </summary>
public class AdProposal : IEntity<int>
{
    public int Id { get; set; }
    public int CurrentVersion { get; set; }
    public AdProposalStatus Status { get; set; }
    public string Platform { get; set; } = null!;
    public string AccountExternalId { get; set; } = null!;
    public string ActionType { get; set; } = null!;
    public string CreatedByPrincipal { get; set; } = null!;
    public AdActorKind CreatedByKind { get; set; }
    public Guid? AgentRunId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExecutionStartedAt { get; set; }
    public DateTime? ExecutionFinishedAt { get; set; }
    public DateTime? RevertedAt { get; set; }

    /// <summary>Optimistic concurrency token; every state change bumps it via <see cref="Touch"/>.</summary>
    public int Revision { get; set; }

    public List<AdProposalVersion> Versions { get; set; } = new();
    public List<AdProposalApproval> Approvals { get; set; } = new();
    public List<AdProposalActionExecution> Executions { get; set; } = new();

    public static AdProposal Create(AdProposalDraft draft, DateTime now, TimeSpan ttl)
    {
        var proposal = new AdProposal
        {
            Status = AdProposalStatus.Pending,
            Platform = draft.Platform,
            AccountExternalId = draft.AccountExternalId,
            ActionType = draft.ActionType,
            CreatedByPrincipal = draft.CreatedByPrincipal,
            CreatedByKind = draft.CreatedByKind,
            AgentRunId = draft.AgentRunId,
            CreatedAt = now,
        };
        proposal.AppendVersion(draft.ActionsJson, draft.ActionCount, draft.Reasoning, draft.CreatedByPrincipal, now, ttl);
        return proposal;
    }

    public AdProposalVersion GetCurrentVersion() => Versions.Single(v => v.Version == CurrentVersion);

    public IReadOnlyList<string> GetCurrentApprovers() => Approvals
        .Where(a => a.Version == CurrentVersion && a.Decision == ApprovalDecision.Approved)
        .Select(a => a.Principal)
        .ToList();

    public bool IsExpiredAt(DateTime now) => Status == AdProposalStatus.Pending && now >= ExpiresAt;

    public void Revise(string actionsJson, int actionCount, string reasoning, string principal, DateTime now, TimeSpan ttl)
    {
        EnsureStatus(AdProposalStatus.Pending);
        AppendVersion(actionsJson, actionCount, reasoning, principal, now, ttl);
    }

    public void RecordApproval(string principal, ApprovalChannel channel, DateTime now, bool isFinal)
    {
        EnsureStatus(AdProposalStatus.Pending);
        Approvals.Add(new AdProposalApproval
        {
            Version = CurrentVersion,
            Principal = principal,
            Channel = channel,
            Decision = ApprovalDecision.Approved,
            At = now,
        });
        if (isFinal)
        {
            TransitionTo(AdProposalStatus.Approved, now);
        }
        else
        {
            Touch(now);
        }
    }

    public void Reject(string principal, ApprovalChannel channel, string reason, DateTime now)
    {
        EnsureStatus(AdProposalStatus.Pending);
        Approvals.Add(new AdProposalApproval
        {
            Version = CurrentVersion,
            Principal = principal,
            Channel = channel,
            Decision = ApprovalDecision.Rejected,
            Reason = reason,
            At = now,
        });
        TransitionTo(AdProposalStatus.Rejected, now);
    }

    public void Expire(DateTime now) => TransitionTo(AdProposalStatus.Expired, now);

    public void StartExecution(DateTime now)
    {
        TransitionTo(AdProposalStatus.Executing, now);
        ExecutionStartedAt = now;
    }

    public void RecordExecution(AdProposalActionExecution execution) => Executions.Add(execution);

    public void FinishExecution(DateTime now)
    {
        var succeeded = Executions.Count(e => e.Version == CurrentVersion && e.State == AdActionExecutionState.Succeeded);
        var total = GetCurrentVersion().ActionCount;
        var target = succeeded == total
            ? AdProposalStatus.Executed
            : succeeded > 0 ? AdProposalStatus.PartiallyExecuted : AdProposalStatus.Failed;
        TransitionTo(target, now);
        ExecutionFinishedAt = now;
    }

    public void MarkReverted(DateTime now)
    {
        TransitionTo(AdProposalStatus.Reverted, now);
        RevertedAt = now;
    }

    public void Touch(DateTime now)
    {
        UpdatedAt = now;
        Revision++;
    }

    private void AppendVersion(string actionsJson, int actionCount, string reasoning, string principal, DateTime now, TimeSpan ttl)
    {
        CurrentVersion++;
        Versions.Add(new AdProposalVersion
        {
            Version = CurrentVersion,
            ActionsJson = actionsJson,
            ActionCount = actionCount,
            Reasoning = reasoning,
            CreatedBy = principal,
            CreatedAt = now,
        });
        ExpiresAt = now + ttl;
        Touch(now);
    }

    private void EnsureStatus(AdProposalStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Ad proposal {Id} is {Status}; the operation requires {expected}.");
        }
    }

    private void TransitionTo(AdProposalStatus target, DateTime now)
    {
        if (!AdProposalStateMachine.CanTransition(Status, target))
        {
            throw new InvalidOperationException($"Ad proposal {Id} cannot move from {Status} to {target}.");
        }
        Status = target;
        Touch(now);
    }
}
