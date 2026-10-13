using System.Globalization;
using System.Net;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ShoptetOrders.DoubleShotSwap;

/// <summary>
/// Replaces double-shot V order lines with their S mirror set inside Shoptet
/// (docs/processes/job-double-shot-order-swap.md). Shoptet has no atomic item swap, so each line is
/// POST S (tagged) then DELETE V; DoubleShotSwapPlanner finishes an interrupted swap next run.
/// Orders a third-party add-on parked in the hold status are swapped first and then released, so no
/// other automation sees them with a V line.
/// </summary>
public sealed class DoubleShotOrderSwapService : IDoubleShotOrderSwapService
{
    internal static readonly int[] DefaultEligibleStatusIds = [-1, 3, 32, -2, 35, 73];
    private static readonly SemaphoreSlim SweepLock = new(1, 1);
    private static readonly TimeSpan SweepLockWait = TimeSpan.FromMinutes(2);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const string NotePrefix = "Automaticky prohozeno: ";
    private const int MinLookbackDays = 1;
    private static readonly TimeSpan HoldAlertAfter = TimeSpan.FromMinutes(15);

    private enum ActionResult { Swapped, Healed, Failed, Abandoned }

    private enum ReleaseResult { NotAttempted, Released, LeftHold, Failed }

    private sealed record OrderOutcome(int Swapped, int Healed, int FailedLines, bool OrderFailed, bool Released = false, bool StillHeld = false)
    {
        public static OrderOutcome None { get; } = new(0, 0, 0, false);
        public static OrderOutcome Failure { get; } = new(0, 0, 0, true);
    }

    private readonly IEshopOrderClient _orderClient;
    private readonly IEshopStockClient _stockClient;
    private readonly ShoptetOrdersSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DoubleShotOrderSwapService> _logger;

    public DoubleShotOrderSwapService(
        IEshopOrderClient orderClient,
        IEshopStockClient stockClient,
        IOptions<ShoptetOrdersSettings> settings,
        TimeProvider clock,
        ILogger<DoubleShotOrderSwapService> logger)
    {
        _orderClient = orderClient;
        _stockClient = stockClient;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    private int? HoldStatusId => _settings.DoubleShotSwapHoldStatusId;

    /// <summary>The configured (or default) pre-picking states, plus the hold status when one is set.</summary>
    private IReadOnlyCollection<int> EligibleStatusIds =>
        (_settings.DoubleShotSwapEligibleStatusIds.Length == 0
            ? DefaultEligibleStatusIds
            : _settings.DoubleShotSwapEligibleStatusIds)
        .Concat(HoldStatusId is { } hold ? [hold] : [])
        .Distinct()
        .ToArray();

    public async Task<DoubleShotSwapSummary> SweepAsync(bool applyChanges, CancellationToken ct = default)
    {
        if (!await SweepLock.WaitAsync(SweepLockWait, ct))
        {
            _logger.LogWarning("DoubleShotSwap: another sweep is still running after {Wait}; skipping this one.", SweepLockWait);
            return DoubleShotSwapSummary.Busy;
        }

        try
        {
            return await SweepLockedAsync(applyChanges, ct);
        }
        finally
        {
            SweepLock.Release();
        }
    }

    private async Task<DoubleShotSwapSummary> SweepLockedAsync(bool applyChanges, CancellationToken ct)
    {
        var (held, holdListingFailed) = await FindHeldOrdersAsync(ct);
        var (candidates, listingsFailed) = await FindCandidateOrdersAsync(ct);
        listingsFailed += holdListingFailed ? 1 : 0;
        // The list status is a cheap pre-filter; the status re-read from the detail call still decides.
        var eligibleCandidates = candidates.Where(o => EligibleStatusIds.Contains(o.StatusId)).ToList();
        // Held orders go first: other automations are waiting on them.
        var heldSince = held.ToDictionary(o => o.Code, o => o.CreationTime, StringComparer.Ordinal);
        var eligibleCodes = held.Concat(eligibleCandidates)
            .Select(o => o.Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var outcomes = new List<OrderOutcome>(eligibleCodes.Count);
        foreach (var orderCode in eligibleCodes)
            outcomes.Add(await ProcessOrderSafelyAsync(orderCode, heldSince.GetValueOrDefault(orderCode), applyChanges, ct));

        var summary = new DoubleShotSwapSummary(
            OrdersScanned: eligibleCodes.Count,
            LinesSwapped: outcomes.Sum(o => o.Swapped),
            LinesHealed: outcomes.Sum(o => o.Healed),
            LinesFailed: outcomes.Sum(o => o.FailedLines),
            OrdersFailed: outcomes.Count(o => o.OrderFailed),
            OrdersNotEligible: candidates.Count - eligibleCandidates.Count,
            ListingsFailed: listingsFailed,
            OrdersReleased: outcomes.Count(o => o.Released),
            OrdersStillHeld: outcomes.Count(o => o.StillHeld));

        _logger.Log(
            listingsFailed > 0 ? LogLevel.Warning : LogLevel.Information,
            "DoubleShotSwap: scanned {Orders} order(s), skipped {NotEligible} not eligible, swapped {Swapped}, healed {Healed}, failed lines {FailedLines}, failed orders {FailedOrders}, failed listings {FailedListings}, released {Released}, still held {StillHeld}{Mode}.",
            summary.OrdersScanned, summary.OrdersNotEligible, summary.LinesSwapped, summary.LinesHealed, summary.LinesFailed, summary.OrdersFailed,
            summary.ListingsFailed, summary.OrdersReleased, summary.OrdersStillHeld, applyChanges ? string.Empty : " [DRY RUN — no changes applied]");
        return summary;
    }

    /// <summary>Returns the orders parked in the hold status (none when no hold status is set), and
    /// whether listing them failed.</summary>
    private async Task<(IReadOnlyList<EshopOrderSummary> Orders, bool Failed)> FindHeldOrdersAsync(CancellationToken ct)
    {
        if (HoldStatusId is not { } holdStatusId)
            return ([], false);

        try
        {
            return (await _orderClient.ListOrdersByStatusAsync(holdStatusId, ct), false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "DoubleShotSwap: listing orders in hold status {HoldStatusId} failed; held orders wait for the next run.", holdStatusId);
            return ([], true);
        }
    }

    /// <summary>Returns the orders holding any V code, plus how many per-code listings failed:
    /// orders behind a failed listing are missed this run, so the summary must say so.</summary>
    private async Task<(IReadOnlyList<EshopOrderSummary> Orders, int ListingsFailed)> FindCandidateOrdersAsync(CancellationToken ct)
    {
        var stock = await _stockClient.ListAsync(ct);
        var variantCodes = stock
            .Select(s => s.Code)
            .Where(DoubleShotCodeRule.IsVariantCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var createdFrom = _clock.GetUtcNow().AddDays(-Math.Max(MinLookbackDays, _settings.DoubleShotSwapLookbackDays));

        var perVariant = new List<IReadOnlyList<EshopOrderSummary>?>(variantCodes.Count);
        foreach (var variantCode in variantCodes)
            perVariant.Add(await ListOrdersSafelyAsync(variantCode, createdFrom, ct));

        var orders = perVariant
            .SelectMany(listed => listed ?? [])
            .DistinctBy(o => o.Code, StringComparer.Ordinal)
            .ToList();
        return (orders, perVariant.Count(listed => listed == null));
    }

    /// <summary>Returns null when the listing failed.</summary>
    private async Task<IReadOnlyList<EshopOrderSummary>?> ListOrdersSafelyAsync(string variantCode, DateTimeOffset createdFrom, CancellationToken ct)
    {
        try
        {
            return await _orderClient.ListOrdersContainingProductAsync(variantCode, createdFrom, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "DoubleShotSwap: listing orders containing {VariantCode} failed; skipping it this run.", variantCode);
            return null;
        }
    }

    private async Task<OrderOutcome> ProcessOrderSafelyAsync(string orderCode, DateTimeOffset? heldSince, bool applyChanges, CancellationToken ct)
    {
        try
        {
            var outcome = await ProcessOrderAsync(orderCode, applyChanges, ct);
            if (outcome.StillHeld && heldSince is null)
                _logger.LogDebug("DoubleShotSwap: held order {OrderCode} has no usable creation time; the stuck-order alert is skipped for it.", orderCode);
            if (outcome.StillHeld && heldSince is { } created && _clock.GetUtcNow() - created > HoldAlertAfter)
                _logger.LogWarning(
                    "DoubleShotSwap: order {OrderCode} has been waiting in hold status {HoldStatusId} since {Created:u}; check it by hand.",
                    orderCode, HoldStatusId, created);
            return outcome;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DoubleShotSwap: order {OrderCode} failed; it will be retried next run.", orderCode);
            return OrderOutcome.Failure;
        }
    }

    private async Task<OrderOutcome> ProcessOrderAsync(string orderCode, bool applyChanges, CancellationToken ct)
    {
        var order = await _orderClient.GetOrderDetailAsync(orderCode, ct);
        if (order == null)
            return OrderOutcome.None;

        var plan = DoubleShotSwapPlanner.Plan(order, EligibleStatusIds);
        if (!plan.IsEligible)
        {
            _logger.LogInformation("DoubleShotSwap: order {OrderCode} is in state {StatusId}, not eligible; left untouched.", orderCode, order.StatusId);
            return OrderOutcome.None;
        }

        foreach (var skipped in plan.Skipped)
            _logger.LogWarning(
                "DoubleShotSwap: order {OrderCode} line {ItemId}: {VariantCode} has amount {Amount} and unit price {UnitPrice}; not swapped, so a paid line is never replaced by a free set. Check the order by hand.",
                orderCode, skipped.ItemId, skipped.VariantCode, skipped.Amount, skipped.UnitPriceWithVat);

        var isHeld = order.StatusId == HoldStatusId;
        if (!applyChanges)
        {
            foreach (var action in plan.Actions)
                _logger.LogInformation(
                    "DoubleShotSwap [DRY RUN]: order {OrderCode} line {ItemId}: would replace {VariantCode} with {SetCode} ({Amount} ks){Heal}.",
                    orderCode, action.VariantItemId, action.VariantCode, action.SetCode, action.Amount, action.IsHeal ? " — finish half swap" : string.Empty);
            if (isHeld && plan.Skipped.Count == 0)
                _logger.LogInformation(
                    "DoubleShotSwap [DRY RUN]: order {OrderCode} is in hold status; would release it to status {ReleaseStatusId}.",
                    orderCode, _settings.DoubleShotSwapReleaseStatusId);
            return OrderOutcome.None with { StillHeld = isHeld };
        }

        var results = new List<(DoubleShotSwapAction Action, ActionResult Result)>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            var result = await ExecuteActionAsync(orderCode, action, ct);
            results.Add((action, result));
            if (result == ActionResult.Abandoned)
                break;
        }

        var done = results.Where(r => r.Result is ActionResult.Swapped or ActionResult.Healed).Select(r => r.Action).ToList();
        if (done.Count > 0)
            await AppendNoteAsync(orderCode, done, ct);

        var failedLines = results.Count(r => r.Result is ActionResult.Failed or ActionResult.Abandoned);
        var isClean = failedLines == 0 && results.Count == plan.Actions.Count && plan.Skipped.Count == 0;
        var release = isHeld && isClean ? await TryReleaseAsync(orderCode, ct) : ReleaseResult.NotAttempted;

        return new OrderOutcome(
            Swapped: results.Count(r => r.Result == ActionResult.Swapped),
            Healed: results.Count(r => r.Result == ActionResult.Healed),
            FailedLines: failedLines,
            OrderFailed: false,
            Released: release == ReleaseResult.Released,
            StillHeld: isHeld && (!isClean || release == ReleaseResult.Failed));
    }

    /// <summary>Moves a held order that no longer holds a V line to the release status, but only if it
    /// is still in the hold status: a status someone else set in the meantime is never overwritten.
    /// The change suppresses the status's customer e-mail and SMS.
    /// A failed release is retried next run (the order is clean, so it is only re-checked).</summary>
    private async Task<ReleaseResult> TryReleaseAsync(string orderCode, CancellationToken ct)
    {
        var releaseStatusId = _settings.DoubleShotSwapReleaseStatusId;
        try
        {
            var currentStatusId = await _orderClient.GetOrderStatusIdAsync(orderCode, ct);
            if (currentStatusId != HoldStatusId)
            {
                _logger.LogInformation(
                    "DoubleShotSwap: order {OrderCode} left hold status for {StatusId} during the swap; not released.",
                    orderCode, currentStatusId);
                return ReleaseResult.LeftHold;
            }

            // The customer already got the order confirmation; the hold round trip must stay silent.
            await _orderClient.UpdateStatusWithoutNotificationsAsync(orderCode, releaseStatusId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DoubleShotSwap: order {OrderCode} is clean but releasing it to status {ReleaseStatusId} failed; next run retries.",
                orderCode, releaseStatusId);
            return ReleaseResult.Failed;
        }

        _logger.LogInformation("DoubleShotSwap: order {OrderCode} released from hold to status {ReleaseStatusId}.", orderCode, releaseStatusId);
        return ReleaseResult.Released;
    }

    private async Task<ActionResult> ExecuteActionAsync(string orderCode, DoubleShotSwapAction action, CancellationToken ct)
    {
        if (action.SetItemToAdd != null)
        {
            var addFailure = await TryAddSetAsync(orderCode, action, action.SetItemToAdd, ct);
            if (addFailure != null)
                return addFailure.Value;
        }

        return await DeleteVariantAsync(orderCode, action, ct);
    }

    /// <summary>POSTs the S line. Returns null on success, otherwise how the action ended.</summary>
    private async Task<ActionResult?> TryAddSetAsync(string orderCode, DoubleShotSwapAction action, NewEshopOrderItem setItem, CancellationToken ct)
    {
        try
        {
            await _orderClient.AddOrderItemAsync(orderCode, setItem, ct);
            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogError(ex,
                "DoubleShotSwap: order {OrderCode} line {ItemId}: Shoptet rejected replacing {VariantCode} with {SetCode} (does the set exist?). The V line stays.",
                orderCode, action.VariantItemId, action.VariantCode, action.SetCode);
            return ActionResult.Failed;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "DoubleShotSwap: order {OrderCode} line {ItemId}: swap interrupted ({Status}); the rest of the order waits for the next run.",
                orderCode, action.VariantItemId, (ex as HttpRequestException)?.StatusCode);
            return ActionResult.Abandoned;
        }
    }

    /// <summary>DELETEs the V line. Runs only once the tagged S line is in the order, so any failure
    /// leaves the order holding both lines: that is an Error, and the order stops here.</summary>
    private async Task<ActionResult> DeleteVariantAsync(string orderCode, DoubleShotSwapAction action, CancellationToken ct)
    {
        try
        {
            await _orderClient.DeleteOrderItemAsync(orderCode, action.VariantItemId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "DoubleShotSwap: order {OrderCode} now contains both {VariantCode} (line {ItemId}) and {SetCode}; deleting the V line failed ({Status}) — next run retries the delete; if it keeps failing remove the V line by hand.",
                orderCode, action.VariantCode, action.VariantItemId, action.SetCode, (ex as HttpRequestException)?.StatusCode);
            return ActionResult.Abandoned;
        }

        _logger.LogInformation(
            "DoubleShotSwap: order {OrderCode} line {ItemId}: replaced {VariantCode} with {SetCode} ({Amount} ks){Heal}.",
            orderCode, action.VariantItemId, action.VariantCode, action.SetCode, action.Amount, action.IsHeal ? " — finished half swap" : string.Empty);
        return action.IsHeal ? ActionResult.Healed : ActionResult.Swapped;
    }

    private async Task AppendNoteAsync(string orderCode, IReadOnlyList<DoubleShotSwapAction> done, CancellationToken ct)
    {
        var note = NotePrefix + string.Join("; ", done.Select(a =>
            $"{a.VariantCode} → {a.SetCode} ({a.Amount.ToString("0.###", Invariant)} ks)"));
        try
        {
            await _orderClient.AppendEshopRemarkAsync(orderCode, note, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "DoubleShotSwap: order {OrderCode} was swapped but the note could not be appended.", orderCode);
        }
    }
}
