using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Domain.Features.MarketingAds;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

/// <summary>
/// Executes one Approved proposal (spec 6.2, 6.6) — the only code path that changes a live ad account.
/// Claims the proposal (Approved → Executing, one SaveChanges guarded by the Revision token), re-evaluates the hard
/// limits (spec 6.3; a Denied proposal runs nothing), then runs the actions in order: kill switch → executor →
/// read current → old-value check → execute → record + save.
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
    public const string ConfirmedByReread = "Applied — confirmed by re-read after transport error: ";
    public const string DeniedByLimits = "Not executed: the limits deny it at execution time: ";
    private const int LockTimeoutSeconds = 600;
    private const int MaxAuditTextLength = 4000;
    private const int MaxValueLength = 64;
    private const int MaxResourceIdLength = 200;

    private readonly IAdProposalRepository _proposals;
    private readonly IAdGovernanceRepository _governance;
    private readonly IAdAuditLog _audit;
    private readonly IEnumerable<IAdActionExecutor> _executors;
    private readonly IAdLimitsContextBuilder _limitsContext;
    private readonly IAdLimitsEvaluator _limits;
    private readonly TimeProvider _time;
    private readonly ILogger<AdProposalExecutionJob> _logger;

    public AdProposalExecutionJob(IAdProposalRepository proposals, IAdGovernanceRepository governance, IAdAuditLog audit,
        IEnumerable<IAdActionExecutor> executors, IAdLimitsContextBuilder limitsContext, IAdLimitsEvaluator limits,
        TimeProvider time, ILogger<AdProposalExecutionJob> logger)
    {
        _proposals = proposals;
        _governance = governance;
        _audit = audit;
        _executors = executors;
        _limitsContext = limitsContext;
        _limits = limits;
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
            stopReason = await FindLimitsDenialAsync(proposal, actions, cancellationToken)
                ?? await RunActionsAsync(proposal, actions, cancellationToken);
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
        var outcome = await _proposals.TrySaveChangesWithOutcomeAsync(cancellationToken);
        if (outcome == AdSaveOutcome.Saved)
        {
            return true;
        }
        if (outcome == AdSaveOutcome.ConcurrentChange)
        {
            _logger.LogInformation("Ad proposal {ProposalId} was claimed by another execution run; this delivery exits.", proposal.Id);
        }
        else
        {
            _logger.LogError("Claiming ad proposal {ProposalId} for execution failed to save; it stays Approved.", proposal.Id);
        }
        return false;
    }

    /// <summary>
    /// Approval checked the limits against the state at that moment; settings, the managed flag and the weekly pause
    /// budget can change before the job runs. Only Denied stops it: web-only and second-approver outcomes were settled
    /// by the approvals that made it Approved. The proposal itself is Executing and excluded from the in-flight count.
    /// </summary>
    /// <returns>The reason recorded on every action when the limits deny the proposal now, otherwise null.</returns>
    private async Task<string?> FindLimitsDenialAsync(AdProposal proposal, IReadOnlyList<AdAction> actions, CancellationToken cancellationToken)
    {
        var evaluation = _limits.Evaluate(await _limitsContext.BuildForExecutionAsync(proposal, actions, cancellationToken));
        if (evaluation.Decision != AdLimitsDecision.Denied)
        {
            return null;
        }
        _logger.LogWarning("Ad proposal {ProposalId} is denied by the limits at execution ({Reason}); nothing is executed.", proposal.Id, evaluation.Reason);
        return DeniedByLimits + evaluation.Reason;
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
            Record(proposal, index, action, OutcomeAfterThrownMutate(action, before, observed, ex));
            return cancelled ? Cancelled : SkippedAfterStop; // never mutate again after a transport fault
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

    /// <summary>The re-read decides: showing the new value means the mutate landed; anything else stays Failed.</summary>
    private static ActionOutcome OutcomeAfterThrownMutate(AdAction action, AdTargetState before, AdTargetState? observed, Exception ex) =>
        observed is not null && string.Equals(observed.CurrentValue, action.NewValue, StringComparison.Ordinal)
            ? new ActionOutcome(AdActionExecutionState.Succeeded, ConfirmedByReread + ex.Message, before,
                ObservedAfter: observed.CurrentValue, ObservedRawJson: observed.RawJson)
            : new ActionOutcome(AdActionExecutionState.Failed, $"Execution failed: {ex.Message} {MayHaveBeenApplied}", before,
                ObservedAfter: observed?.CurrentValue);

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
        var (resourceId, responseJson) = FitResourceId(proposal.Id, index, result?.PlatformResourceId,
            result?.PlatformResponseJson ?? outcome.ObservedRawJson);
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
            PlatformResourceId = resourceId,
            PlatformResponseJson = responseJson,
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
                platformResponse = Truncate(responseJson, MaxAuditTextLength),
                error = Truncate(outcome.Error, MaxAuditTextLength),
                validateOnly = isValidateOnly,
            }, now));
    }

    /// <summary>
    /// A resource id longer than its column would fail the save right after a live mutate, and a clipped one would break
    /// revert; such an id is kept in the response JSON instead and the column stays null.
    /// </summary>
    private (string? ResourceId, string? ResponseJson) FitResourceId(int proposalId, int index, string? resourceId, string? responseJson)
    {
        if (resourceId is null || resourceId.Length <= MaxResourceIdLength)
        {
            return (resourceId, responseJson);
        }
        _logger.LogWarning("Ad proposal {ProposalId} action {Index}: platform resource id is {Length} characters (max {Max}); kept in the response JSON only.",
            proposalId, index, resourceId.Length, MaxResourceIdLength);
        if (responseJson is not null && responseJson.Contains(resourceId, StringComparison.Ordinal))
        {
            return (null, responseJson);
        }
        var envelope = new JsonObject { ["platformResourceId"] = resourceId, ["platformResponse"] = ParseOrText(responseJson) };
        return (null, envelope.ToJsonString());
    }

    private static JsonNode? ParseOrText(string? json)
    {
        if (json is null)
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    private static string? Clip(string? value) => Truncate(value, MaxValueLength);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    /// <param name="ObservedAfter">Value re-read after a thrown mutate; used when there is no platform result.</param>
    /// <param name="ObservedRawJson">Raw re-read state, stored as the response when the re-read confirms the change.</param>
    private sealed record ActionOutcome(
        AdActionExecutionState State, string? Error, AdTargetState? Before = null, AdExecutionResult? Result = null,
        string? ObservedAfter = null, string? ObservedRawJson = null);
}
