using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.RevertAdProposal;

/// <summary>
/// A human (web UI, Marketing_AdApprovals Admin) restores the account from the stored before-state: every
/// succeeded, not-yet-reverted action of the executed version is reverted in reverse order, each result saved and
/// audited on its own. Not gated by the kill switch. When anything is left, the proposal keeps its status and the
/// response is MarketingAdsRevertFailed; a retry reverts only what is left, and first re-reads a row whose earlier
/// revert failed, so a revert that landed after all is not sent again.
/// Two admins reverting concurrently can both reach the platform (accepted; the re-read makes retries converge), but
/// every per-row save bumps the proposal's Revision token, so a request working on a stale copy never overwrites a
/// newer row state: it stops with MarketingAdsConcurrentChange instead.
/// </summary>
public class RevertAdProposalHandler : IRequestHandler<RevertAdProposalRequest, RevertAdProposalResponse>
{
    public const string MayHaveBeenApplied = "The revert may have been applied; verify in the account.";
    public const string ResourceIdUnknown = "Cannot revert automatically: platform resource id unknown — remove the negative keyword manually.";
    public const string ConfirmedByReread = "Already reverted — confirmed by re-read: the current value matches the original.";
    public const string Cancelled = "Cancelled: the request stopped during the revert.";
    private const int MaxAuditTextLength = 4000;

    private readonly IAdProposalRepository _proposals;
    private readonly IAdAuditLog _audit;
    private readonly IEnumerable<IAdActionExecutor> _executors;
    private readonly IApprovalChannelResolver _channelResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<RevertAdProposalHandler> _logger;

    public RevertAdProposalHandler(IAdProposalRepository proposals, IAdAuditLog audit, IEnumerable<IAdActionExecutor> executors,
        IApprovalChannelResolver channelResolver, ICurrentUserService currentUser, TimeProvider time, ILogger<RevertAdProposalHandler> logger)
    {
        _proposals = proposals;
        _audit = audit;
        _executors = executors;
        _channelResolver = channelResolver;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<RevertAdProposalResponse> Handle(RevertAdProposalRequest request, CancellationToken cancellationToken)
    {
        var check = AdWebAdminGuard.Check(_currentUser, _channelResolver);
        if (!check.IsAllowed) return new RevertAdProposalResponse(check.ErrorCode!.Value, check.Params);

        var proposal = await _proposals.GetForUpdateAsync(request.Id, cancellationToken);
        if (proposal is null) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsProposalNotFound);
        if (proposal.Status is not (AdProposalStatus.Executed or AdProposalStatus.PartiallyExecuted)) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsNotRevertible);
        var executor = _executors.FirstOrDefault(e => e.Platform.ToString() == proposal.Platform
            && e.SupportedActions.Any(type => type.ToString() == proposal.ActionType));
        if (executor is null) return new RevertAdProposalResponse(ErrorCodes.MarketingAdsNoExecutor);

        proposal.Touch(Now()); // claims the proposal: a request that loaded it before this save fails on the Revision token
        if (!await _proposals.TrySaveChangesAsync(cancellationToken))
        {
            _logger.LogWarning("Concurrent change while reverting ad proposal {ProposalId}", request.Id);
            return new RevertAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }

        var actions = AdActionJson.Deserialize(proposal.GetCurrentVersion().ActionsJson);
        var pending = proposal.Executions
            .Where(e => e.Version == proposal.CurrentVersion && e.State == AdActionExecutionState.Succeeded && e.RevertState != AdActionRevertState.Reverted)
            .OrderByDescending(e => e.ActionIndex)
            .ToList();
        var tally = await RevertAllAsync(proposal, pending, actions, executor, check.Principal!, cancellationToken);

        if (tally.LostRace)
        {
            return new RevertAdProposalResponse(ErrorCodes.MarketingAdsConcurrentChange);
        }
        if (tally.Failed > 0)
        {
            _logger.LogWarning("Revert of ad proposal {ProposalId} left {Failed} action(s) unreverted ({ValidateOnly} validate-only).",
                proposal.Id, tally.Failed, tally.ValidateOnly);
            return ToResponse(new RevertAdProposalResponse(ErrorCodes.MarketingAdsRevertFailed, new Dictionary<string, string>
            {
                ["Reverted"] = tally.Reverted.ToString(),
                ["Failed"] = tally.Failed.ToString(),
                ["ValidateOnly"] = tally.ValidateOnly.ToString(),
            }), proposal, tally);
        }

        proposal.MarkReverted(Now());
        await _proposals.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Ad proposal {ProposalId} reverted by {Principal}.", proposal.Id, check.Principal);
        return ToResponse(new RevertAdProposalResponse(), proposal, tally);
    }

    private async Task<RevertTally> RevertAllAsync(AdProposal proposal, IReadOnlyList<AdProposalActionExecution> rows,
        IReadOnlyList<AdAction> actions, IAdActionExecutor executor, string principal, CancellationToken cancellationToken)
    {
        var tally = new RevertTally(0, 0, 0, false);
        foreach (var row in rows)
        {
            var outcome = await RevertRowAsync(proposal.Id, row, actions[row.ActionIndex], executor, cancellationToken);
            Record(proposal, row, outcome, principal);
            proposal.Touch(Now()); // a concurrent revert that saved first makes this save fail instead of overwriting its row
            if (!await _proposals.TrySaveChangesAsync(CancellationToken.None)) // the platform call happened; keep its result unless outdated
            {
                _logger.LogWarning("Concurrent revert of ad proposal {ProposalId}; action {Index} result not stored (reverted: {Reverted}, detail: {Detail}).",
                    proposal.Id, row.ActionIndex, outcome.IsReverted, outcome.Detail);
                return tally with { LostRace = true };
            }
            tally = outcome.IsReverted
                ? tally with { Reverted = tally.Reverted + 1 }
                : tally with { Failed = tally.Failed + 1, ValidateOnly = tally.ValidateOnly + (outcome.IsValidateOnly ? 1 : 0) };
            cancellationToken.ThrowIfCancellationRequested();
        }
        return tally;
    }

    private async Task<RowOutcome> RevertRowAsync(int proposalId, AdProposalActionExecution row, AdAction action,
        IAdActionExecutor executor, CancellationToken cancellationToken)
    {
        if (row.RevertState == AdActionRevertState.RevertFailed
            && await TryReadRevertedStateAsync(proposalId, row, action, executor, cancellationToken) is { } rawJson)
        {
            return new RowOutcome(true, ConfirmedByReread, rawJson, IsConfirmedByReread: true);
        }

        var original = Original(row, action);
        if (action.Type == AdActionType.AddNegativeKeyword && original.PlatformResourceId is null)
        {
            return new RowOutcome(false, ResourceIdUnknown, null);
        }

        try
        {
            var result = await executor.RevertAsync(action, original, cancellationToken);
            return result.Outcome == AdExecutionOutcome.Succeeded
                ? new RowOutcome(true, null, result.PlatformResponseJson)
                : new RowOutcome(false, result.Error ?? result.Outcome.ToString(), result.PlatformResponseJson,
                    IsValidateOnly: AdExecutionErrors.IsValidateOnly(result.Error));
        }
        catch (Exception ex)
        {
            // Executors throw only for transport/auth, and a thrown mutate may still have landed.
            var cancelled = cancellationToken.IsCancellationRequested;
            _logger.LogError(ex, "Revert failed for ad proposal {ProposalId} action {Index} (cancelled: {Cancelled})", proposalId, row.ActionIndex, cancelled);
            var reason = cancelled ? Cancelled : $"Revert failed: {ex.Message}";
            return new RowOutcome(false, $"{reason} {MayHaveBeenApplied}", null);
        }
    }

    /// <returns>The re-read state's raw JSON when the target already shows its original value; otherwise null.</returns>
    private async Task<string?> TryReadRevertedStateAsync(int proposalId, AdProposalActionExecution row, AdAction action,
        IAdActionExecutor executor, CancellationToken cancellationToken)
    {
        try
        {
            var state = await executor.ReadCurrentAsync(action, cancellationToken);
            return string.Equals(state.CurrentValue, action.OldValue, StringComparison.Ordinal) ? state.RawJson ?? "{}" : null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Re-reading ad proposal {ProposalId} action {Index} before a revert retry failed; retrying the revert.", proposalId, row.ActionIndex);
            return null;
        }
    }

    /// <summary>The execution result as the executor returned it, rebuilt from the stored row.</summary>
    private static AdExecutionResult Original(AdProposalActionExecution row, AdAction action)
    {
        var resourceId = row.PlatformResourceId
            ?? (action.Type == AdActionType.AddNegativeKeyword ? StoredResourceId.Recover(row.PlatformResponseJson) : null);
        return new AdExecutionResult(AdExecutionOutcome.Succeeded, row.BeforeValue, row.AfterValue, resourceId, row.PlatformResponseJson, null);
    }

    private void Record(AdProposal proposal, AdProposalActionExecution row, RowOutcome outcome, string principal)
    {
        var now = Now();
        if (outcome.IsReverted)
        {
            row.MarkReverted(now, outcome.ResponseJson);
        }
        else
        {
            row.MarkRevertFailed(now, outcome.Detail!);
        }
        _audit.Append(AdAuditEvents.ForProposal(proposal, outcome.IsReverted ? AdAuditEventType.Reverted : AdAuditEventType.ActionFailed,
            principal, AdActorKind.Human, ApprovalChannel.Web,
            new
            {
                phase = "revert",
                actionIndex = row.ActionIndex,
                target = row.TargetExternalId,
                detail = Truncate(outcome.Detail, MaxAuditTextLength),
                platformResponse = Truncate(outcome.ResponseJson, MaxAuditTextLength),
                validateOnly = outcome.IsValidateOnly,
                confirmedByReread = outcome.IsConfirmedByReread,
            }, now));
    }

    private static RevertAdProposalResponse ToResponse(RevertAdProposalResponse response, AdProposal proposal, RevertTally tally)
    {
        response.ProposalId = proposal.Id;
        response.Status = proposal.Status;
        response.RevertedActions = tally.Reverted;
        response.FailedActions = tally.Failed;
        response.ValidateOnlyActions = tally.ValidateOnly;
        return response;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private sealed record RevertTally(int Reverted, int Failed, int ValidateOnly, bool LostRace);

    /// <param name="Detail">The failure reason (stored as RevertError), or a note on how the revert was confirmed.</param>
    private sealed record RowOutcome(bool IsReverted, string? Detail, string? ResponseJson,
        bool IsValidateOnly = false, bool IsConfirmedByReread = false);
}
