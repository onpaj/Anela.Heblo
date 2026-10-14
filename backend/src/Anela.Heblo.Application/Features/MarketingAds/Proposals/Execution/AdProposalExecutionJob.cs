using System.Runtime.ExceptionServices;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Domain.Features.MarketingAds;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

/// <summary>
/// Executes one Approved proposal (spec 6.2, 6.6) — the only code path that changes a live ad account.
/// Claims the proposal (Approved → Executing, one SaveChanges guarded by the Revision token), then runs the
/// actions in order: kill switch → executor → read current → old-value check → execute → record + save.
/// Whatever happens, the proposal is finalised (never left Executing) and a thrown mutate is never retried.
/// </summary>
public class AdProposalExecutionJob
{
    public const string KillSwitchOff = "Execution is disabled by the kill switch.";
    public const string NoExecutor = "No executor is registered for this platform and action type.";
    public const string SkippedAfterStop = "Skipped: an earlier action stopped the execution.";
    public const string Cancelled = "Cancelled: the job stopped before this action ran.";
    public const string StoppedUnexpectedly = "Skipped: the execution stopped on an unexpected error before this action ran.";
    public const string NotRecorded = "Not recorded: the execution stopped on an unexpected error. The change may have been applied; verify in the account.";
    public const string MayHaveBeenApplied = "The change may have been applied; verify in the account.";
    private const int LockTimeoutSeconds = 600;
    private const int MaxAuditTextLength = 4000;
    private const int MaxValueLength = 64;

    private readonly IAdProposalRepository _proposals;
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IEnumerable<IAdActionExecutor> _executors;
    private readonly TimeProvider _time;
    private readonly ILogger<AdProposalExecutionJob> _logger;

    public AdProposalExecutionJob(IAdProposalRepository proposals, IAdGovernanceRepository governance, IAdAuditLog audit,
        IEnumerable<IAdActionExecutor> executors, TimeProvider time, ILogger<AdProposalExecutionJob> logger)
    {
        _proposals = proposals;
        _governance = governance;
        _audit = audit;
        _executors = executors;
        _time = time;
        _logger = logger;
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(int proposalId, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetForUpdateAsync(proposalId, cancellationToken);
        if (proposal is null || proposal.Status != AdProposalStatus.Approved)
        {
            _logger.LogInformation("Ad proposal {ProposalId} is {Status}; nothing to execute.", proposalId, proposal?.Status);
            return;
        }
        if (!await TryClaimAsync(proposal, cancellationToken))
        {
            return;
        }

        IReadOnlyList<AdAction> actions = Array.Empty<AdAction>();
        Exception? failure = null;
        string? stopReason;
        try
        {
            actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
            stopReason = await RunActionsAsync(proposal, actions, cancellationToken);
        }
        catch (Exception ex)
        {
            // Anything outside the per-action handling (kill-switch read, a SaveChanges, bad JSON): never strand Executing.
            failure = ex;
            stopReason = cancellationToken.IsCancellationRequested ? Cancelled : StoppedUnexpectedly;
            _logger.LogError(ex, "Execution of ad proposal {ProposalId} stopped unexpectedly; finalising it.", proposalId);
        }

        await FinaliseAsync(proposal, actions, stopReason);
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw(); // the proposal is final; let Hangfire show the run failed
        }
    }

    private async Task<bool> TryClaimAsync(AdProposal proposal, CancellationToken cancellationToken)
    {
        var now = Now();
        proposal.StartExecution(now);
        _audit.Append(AdAuditEvents.ForProposal(proposal, AdAuditEventType.ExecutionStarted, AdSystemPrincipals.Executor,
            AdActorKind.System, ApprovalChannel.System, new { actionCount = proposal.GetCurrentVersion().ActionCount }, now));
        if (await _proposals.TrySaveChangesAsync(cancellationToken))
        {
            return true;
        }
        _logger.LogInformation("Ad proposal {ProposalId} was claimed by another execution run; this delivery exits.", proposal.Id);
        return false;
    }

    /// <returns>The reason for the actions that did not run, or null when every action ran.</returns>
    private async Task<string?> RunActionsAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            string? stopReason;
            try
            {
                stopReason = await ExecuteActionAsync(proposal, index, actions[index], cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                Record(proposal, index, actions[index], new ActionOutcome(AdActionExecutionState.Skipped, Cancelled));
                stopReason = Cancelled;
            }
            await _proposals.SaveChangesAsync(CancellationToken.None);
            if (stopReason is not null)
            {
                return stopReason;
            }
        }
        return null;
    }

    /// <returns>Null to continue with the next action, otherwise the reason recorded for the remaining actions.</returns>
    private async Task<string?> ExecuteActionAsync(AdProposal proposal, int index, AdAction action, CancellationToken cancellationToken)
    {
        if (!AdKillSwitchPolicy.IsExecutionEnabled(await _governance.GetKillSwitchesAsync(cancellationToken), proposal.Platform))
        {
            Record(proposal, index, action, new ActionOutcome(AdActionExecutionState.Skipped, KillSwitchOff));
            return SkippedAfterStop;
        }
        var executor = _executors.FirstOrDefault(e => e.Platform.ToString() == proposal.Platform && e.SupportedActions.Contains(action.Type));
        if (executor is null)
        {
            Record(proposal, index, action, new ActionOutcome(AdActionExecutionState.Failed, NoExecutor));
            return SkippedAfterStop;
        }

        AdTargetState before;
        try
        {
            before = await executor.ReadCurrentAsync(action, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Reading the current state failed for ad proposal {ProposalId} action {Index}", proposal.Id, index);
            Record(proposal, index, action, new ActionOutcome(AdActionExecutionState.Failed, $"Reading the current state failed: {ex.Message}"));
            return SkippedAfterStop;
        }

        if (!string.Equals(before.CurrentValue, action.OldValue, StringComparison.Ordinal))
        {
            Record(proposal, index, action, new ActionOutcome(AdActionExecutionState.StaleState,
                $"Current value '{before.CurrentValue}' differs from the proposal's old value '{action.OldValue}'.", before));
            return SkippedAfterStop;
        }

        return await MutateAsync(proposal, index, action, executor, before, cancellationToken);
    }

    private async Task<string?> MutateAsync(AdProposal proposal, int index, AdAction action, IAdActionExecutor executor,
        AdTargetState before, CancellationToken cancellationToken)
    {
        AdExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(action, cancellationToken);
        }
        catch (Exception ex)
        {
            // The mutate may have landed (timeouts and 5xx are not retried by the adapter): never retry it here.
            var cancelled = cancellationToken.IsCancellationRequested;
            _logger.LogError(ex, "Execute failed for ad proposal {ProposalId} action {Index} (cancelled: {Cancelled})", proposal.Id, index, cancelled);
            var observed = cancelled ? null : await TryRereadAsync(executor, action, proposal.Id, index, cancellationToken);
            Record(proposal, index, action, new ActionOutcome(AdActionExecutionState.Failed,
                $"Execution failed: {ex.Message} {MayHaveBeenApplied}", before, ObservedAfter: observed?.CurrentValue));
            return cancelled ? Cancelled : SkippedAfterStop;
        }

        var state = result.Outcome switch
        {
            AdExecutionOutcome.Succeeded => AdActionExecutionState.Succeeded,
            AdExecutionOutcome.StaleState => AdActionExecutionState.StaleState,
            _ => AdActionExecutionState.Failed,
        };
        if (state == AdActionExecutionState.Failed && AdExecutionErrors.IsValidateOnly(result.Error))
        {
            _logger.LogInformation("Ad proposal {ProposalId} action {Index} was only validated; nothing changed in the account.", proposal.Id, index);
        }
        Record(proposal, index, action, new ActionOutcome(state, result.Error, before, result));
        return state == AdActionExecutionState.StaleState ? SkippedAfterStop : null;
    }

    /// <summary>One best-effort read after a thrown mutate, so a human can see whether it landed. Never throws.</summary>
    private async Task<AdTargetState?> TryRereadAsync(IAdActionExecutor executor, AdAction action, int proposalId, int index, CancellationToken cancellationToken)
    {
        try
        {
            return await executor.ReadCurrentAsync(action, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Re-reading the state after a failed execute failed for ad proposal {ProposalId} action {Index}", proposalId, index);
            return null;
        }
    }

    private async Task FinaliseAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, string? stopReason)
    {
        try
        {
            RecordMissing(proposal, actions, stopReason ?? SkippedAfterStop);
            proposal.FinishExecution(Now());
            await _proposals.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Ad proposal {ProposalId} finished as {Status}.", proposal.Id, proposal.Status);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finalising ad proposal {ProposalId} failed; retrying on a fresh load.", proposal.Id);
            _proposals.DiscardChanges();
        }
        await FinaliseFromReloadAsync(proposal.Id, actions);
    }

    /// <summary>The tracked state is unusable; whatever was not stored is recorded with a verify-in-the-account note.</summary>
    private async Task FinaliseFromReloadAsync(int proposalId, IReadOnlyList<AdAction> actions)
    {
        try
        {
            var fresh = await _proposals.GetForUpdateAsync(proposalId, CancellationToken.None);
            if (fresh is null || fresh.Status != AdProposalStatus.Executing)
            {
                _logger.LogWarning("Ad proposal {ProposalId} is {Status} on reload; not finalising it.", proposalId, fresh?.Status);
                return;
            }
            RecordMissing(fresh, actions, NotRecorded);
            fresh.FinishExecution(Now());
            await _proposals.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Ad proposal {ProposalId} finalised as {Status} after a failed save.", proposalId, fresh.Status);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Ad proposal {ProposalId} could not be finalised and is left Executing; fix it by hand.", proposalId);
            _proposals.DiscardChanges();
        }
    }

    private void RecordMissing(AdProposal proposal, IReadOnlyList<AdAction> actions, string reason)
    {
        var recorded = proposal.Executions.Where(e => e.Version == proposal.CurrentVersion).Select(e => e.ActionIndex).ToHashSet();
        for (var index = 0; index < actions.Count; index++)
        {
            if (!recorded.Contains(index))
            {
                Record(proposal, index, actions[index], new ActionOutcome(AdActionExecutionState.Skipped, reason));
            }
        }
    }

    private void Record(AdProposal proposal, int index, AdAction action, ActionOutcome outcome)
    {
        var now = Now();
        var result = outcome.Result;
        var afterValue = Clip(result?.AfterValue ?? outcome.ObservedAfter);
        var isValidateOnly = outcome.State == AdActionExecutionState.Failed && AdExecutionErrors.IsValidateOnly(outcome.Error);
        proposal.RecordExecution(new AdProposalActionExecution
        {
            ProposalId = proposal.Id,
            Version = proposal.CurrentVersion,
            ActionIndex = index,
            Platform = proposal.Platform,
            AccountExternalId = proposal.AccountExternalId,
            ActionType = proposal.ActionType,
            TargetLevel = action.TargetLevel.ToString(),
            TargetExternalId = action.TargetExternalId,
            PayloadJson = JsonSerializer.Serialize(action.Payload),
            State = outcome.State,
            BeforeValue = Clip(outcome.Before?.CurrentValue),
            BeforeRawJson = outcome.Before?.RawJson,
            AfterValue = afterValue,
            PlatformResourceId = result?.PlatformResourceId,
            PlatformResponseJson = result?.PlatformResponseJson,
            Error = outcome.Error,
            ExecutedAt = now,
        });
        var eventType = outcome.State == AdActionExecutionState.Succeeded ? AdAuditEventType.ActionExecuted : AdAuditEventType.ActionFailed;
        _audit.Append(AdAuditEvents.ForProposal(proposal, eventType, AdSystemPrincipals.Executor, AdActorKind.System, ApprovalChannel.System,
            new
            {
                actionIndex = index,
                target = action.TargetExternalId,
                state = outcome.State,
                before = outcome.Before?.CurrentValue,
                after = afterValue,
                platformResourceId = result?.PlatformResourceId,
                platformResponse = Truncate(result?.PlatformResponseJson, MaxAuditTextLength),
                error = Truncate(outcome.Error, MaxAuditTextLength),
                validateOnly = isValidateOnly,
            }, now));
    }

    private static string? Clip(string? value) => Truncate(value, MaxValueLength);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    /// <param name="ObservedAfter">State re-read after a thrown mutate; used when there is no platform result.</param>
    private sealed record ActionOutcome(
        AdActionExecutionState State, string? Error, AdTargetState? Before = null, AdExecutionResult? Result = null, string? ObservedAfter = null);
}
