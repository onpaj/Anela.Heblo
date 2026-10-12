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
/// </summary>
public sealed class DoubleShotOrderSwapService : IDoubleShotOrderSwapService
{
    internal static readonly int[] DefaultEligibleStatusIds = [-1, 3, 32, -2, 35, 73];
    private static readonly SemaphoreSlim SweepLock = new(1, 1);
    private static readonly TimeSpan SweepLockWait = TimeSpan.FromMinutes(2);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const string NotePrefix = "Automaticky prohozeno: ";
    private const int MinLookbackDays = 1;

    private enum ActionResult { Swapped, Healed, Failed, Abandoned }

    private sealed record OrderOutcome(int Swapped, int Healed, int FailedLines, bool OrderFailed)
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

    private IReadOnlyCollection<int> EligibleStatusIds =>
        _settings.DoubleShotSwapEligibleStatusIds.Length == 0
            ? DefaultEligibleStatusIds
            : _settings.DoubleShotSwapEligibleStatusIds.Distinct().ToArray();

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
        var candidates = await FindCandidateOrdersAsync(ct);
        // The list status is a cheap pre-filter; the status re-read from the detail call still decides.
        var eligibleCodes = candidates.Where(o => EligibleStatusIds.Contains(o.StatusId)).Select(o => o.Code).ToList();
        var outcomes = new List<OrderOutcome>(eligibleCodes.Count);
        foreach (var orderCode in eligibleCodes)
            outcomes.Add(await ProcessOrderSafelyAsync(orderCode, applyChanges, ct));

        var summary = new DoubleShotSwapSummary(
            OrdersScanned: eligibleCodes.Count,
            LinesSwapped: outcomes.Sum(o => o.Swapped),
            LinesHealed: outcomes.Sum(o => o.Healed),
            LinesFailed: outcomes.Sum(o => o.FailedLines),
            OrdersFailed: outcomes.Count(o => o.OrderFailed),
            OrdersNotEligible: candidates.Count - eligibleCodes.Count);

        _logger.LogInformation(
            "DoubleShotSwap: scanned {Orders} order(s), skipped {NotEligible} not eligible, swapped {Swapped}, healed {Healed}, failed lines {FailedLines}, failed orders {FailedOrders}{Mode}.",
            summary.OrdersScanned, summary.OrdersNotEligible, summary.LinesSwapped, summary.LinesHealed, summary.LinesFailed, summary.OrdersFailed,
            applyChanges ? string.Empty : " [DRY RUN — no changes applied]");
        return summary;
    }

    private async Task<IReadOnlyList<EshopOrderSummary>> FindCandidateOrdersAsync(CancellationToken ct)
    {
        var stock = await _stockClient.ListAsync(ct);
        var variantCodes = stock
            .Select(s => s.Code)
            .Where(DoubleShotCodeRule.IsVariantCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var createdFrom = _clock.GetUtcNow().AddDays(-Math.Max(MinLookbackDays, _settings.DoubleShotSwapLookbackDays));

        var perVariant = new List<IReadOnlyList<EshopOrderSummary>>(variantCodes.Count);
        foreach (var variantCode in variantCodes)
            perVariant.Add(await ListOrdersSafelyAsync(variantCode, createdFrom, ct));

        return perVariant
            .SelectMany(orders => orders)
            .DistinctBy(o => o.Code, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<EshopOrderSummary>> ListOrdersSafelyAsync(string variantCode, DateTimeOffset createdFrom, CancellationToken ct)
    {
        try
        {
            return await _orderClient.ListOrdersContainingProductAsync(variantCode, createdFrom, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "DoubleShotSwap: listing orders containing {VariantCode} failed; skipping it this run.", variantCode);
            return [];
        }
    }

    private async Task<OrderOutcome> ProcessOrderSafelyAsync(string orderCode, bool applyChanges, CancellationToken ct)
    {
        try
        {
            return await ProcessOrderAsync(orderCode, applyChanges, ct);
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

        if (!applyChanges)
        {
            foreach (var action in plan.Actions)
                _logger.LogInformation(
                    "DoubleShotSwap [DRY RUN]: order {OrderCode} line {ItemId}: would replace {VariantCode} with {SetCode} ({Amount} ks){Heal}.",
                    orderCode, action.VariantItemId, action.VariantCode, action.SetCode, action.Amount, action.IsHeal ? " — finish half swap" : string.Empty);
            return OrderOutcome.None;
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

        return new OrderOutcome(
            Swapped: results.Count(r => r.Result == ActionResult.Swapped),
            Healed: results.Count(r => r.Result == ActionResult.Healed),
            FailedLines: results.Count(r => r.Result is ActionResult.Failed or ActionResult.Abandoned),
            OrderFailed: false);
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
