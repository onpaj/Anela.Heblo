using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;

/// <summary>
/// Heblo-originated platform changes in a window, for C2's out-of-band matcher. Times are stored as UTC
/// 'timestamp' (Kind Unspecified on read), so bounds are converted to UTC going in and re-stamped as UTC coming out.
/// </summary>
public sealed class AdExecutionLookup : IAdExecutionLookup
{
    private readonly IAdProposalRepository _proposals;

    public AdExecutionLookup(IAdProposalRepository proposals) => _proposals = proposals;

    public async Task<IReadOnlyList<AdExecutedAction>> GetExecutedActionsAsync(AdPlatform platform, string accountExternalId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var rows = await _proposals.GetExecutionsInWindowAsync(platform.ToString(), accountExternalId, fromUtc, toUtc, ct);

        var executions = rows
            .Where(r => r.State == AdActionExecutionState.Succeeded && r.ExecutedAt >= fromUtc && r.ExecutedAt <= toUtc)
            .Select(r => ToAction(r, platform, r.ExecutedAt, isRevert: false));
        // A revert is its own Heblo-originated platform change (ad re-enabled / negative removed), so C2's matcher
        // must see it too, otherwise every revert is flagged out-of-band.
        var reverts = rows
            .Where(r => r.RevertState == AdActionRevertState.Reverted && r.RevertedAt is { } at && at >= fromUtc && at <= toUtc)
            .Select(r => ToAction(r, platform, r.RevertedAt!.Value, isRevert: true));
        return executions.Concat(reverts).ToList();
    }

    private static AdExecutedAction ToAction(AdProposalActionExecution r, AdPlatform platform, DateTime atUtc, bool isRevert) =>
        new(
            r.Id.ToString(CultureInfo.InvariantCulture),
            Enum.Parse<AdActionType>(r.ActionType),
            platform,
            r.AccountExternalId,
            Enum.Parse<AdEntityLevel>(r.TargetLevel),
            r.TargetExternalId,
            r.PlatformResourceId,
            new DateTimeOffset(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc)),
            JsonSerializer.Deserialize<Dictionary<string, string>>(r.PayloadJson) ?? new Dictionary<string, string>(),
            IsRevert: isRevert);
}
