using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

/// <summary>
/// Propagates Shoptet's retail price into Flexi for one selection of products. Shoptet is the
/// retail source of truth, so a sync only ever writes the ERP — the shop is never written,
/// which is what separates this from <c>SetProductPriceHandler</c> (an operator changing a
/// price, written to both systems).
///
/// A READ failure before anything has been written propagates, exactly as
/// <c>GetPriceDivergenceReportHandler</c> leaves it: there is no partial state to report. Once
/// a write has landed nothing is allowed to turn the run into a reported total failure — a
/// failed write is counted, and even the confirming re-read is allowed to fail without taking
/// the counts with it.
/// </summary>
public class SyncProductPricesHandler : IRequestHandler<SyncProductPricesRequest, SyncProductPricesResponse>
{
    /// <summary>
    /// How long the loop may keep starting writes. The gateway in front of the app gives up on
    /// a request at 230 s, and a sync that runs past that reports a failure to an operator
    /// whose prices actually reached the ERP. Stopping early instead lets the response say how
    /// many rows are left, and a second run picks them up — a row already written comes back in
    /// agreement and is skipped, so repeated runs converge instead of repeating work.
    /// </summary>
    private static readonly TimeSpan WriteBudget = TimeSpan.FromMinutes(2);

    private readonly IPriceComparisonService _comparisonService;
    private readonly IProductPriceErpClient _erpReader;
    private readonly IErpPriceWriter _erpWriter;
    private readonly IProductPriceChangeLogRepository _changeLog;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SyncProductPricesHandler> _logger;

    public SyncProductPricesHandler(
        IPriceComparisonService comparisonService,
        IProductPriceErpClient erpReader,
        IErpPriceWriter erpWriter,
        IProductPriceChangeLogRepository changeLog,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        ILogger<SyncProductPricesHandler> logger)
    {
        _comparisonService = comparisonService;
        _erpReader = erpReader;
        _erpWriter = erpWriter;
        _changeLog = changeLog;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SyncProductPricesResponse> Handle(
        SyncProductPricesRequest request, CancellationToken cancellationToken)
    {
        var report = await _comparisonService.BuildScopedReportAsync(request.ProductCodes, cancellationToken);

        var targets = report.Rows.Where(NeedsFlexiWrite).ToList();
        if (targets.Count == 0)
        {
            return new SyncProductPricesResponse { Rows = report.Rows };
        }

        var erpItemIds = await ResolveErpItemIdsAsync(cancellationToken);
        var (written, failed, remaining) = await WriteAllAsync(targets, erpItemIds, cancellationToken);

        return new SyncProductPricesResponse
        {
            Rows = await ReadBackAsync(request.ProductCodes, report, written, cancellationToken),
            WrittenCount = written,
            FailedCount = failed,
            RemainingCount = remaining,
        };
    }

    /// <summary>
    /// Only a row the fresh comparison classified <see cref="PriceDivergenceKind.FlexiDiffers"/>
    /// is written. An in-agreement row would spend a live ERP call storing the number Flexi
    /// already holds, and a row missing in Shoptet or in Flexi has nothing to push or nowhere to
    /// push it (<see cref="IErpPriceWriter"/> addresses ceník items by internal id, because
    /// writing by code creates new ones).
    ///
    /// The two "unknown" kinds are deliberately NOT written, however tempting it is: the write
    /// does declare <c>typCeny.sDph</c> alongside the price, but the unknown-ness lives in what
    /// the ERP READ exposes (a company whose user query 41 omits <c>typcenydphk</c>, a VAT band
    /// the adapter does not recognise). Writing does not change that, so the row would come back
    /// unknown and be written again on every single sync, for ever.
    ///
    /// A non-positive Shoptet price is left alone for the same reason the writer refuses it:
    /// zero is not a price to propagate into a live ERP. The row stays visibly divergent.
    /// </summary>
    private static bool NeedsFlexiWrite(PriceDivergenceRowDto row) =>
        row.Kind == PriceDivergenceKind.FlexiDiffers && row.ShoptetPriceWithVat > 0m;

    /// <summary>
    /// The comparison rows carry no ceník id, and addressing Flexi by product code would
    /// create price list items. Read from the cache the scoped report has just force-reloaded,
    /// so this costs nothing and sees exactly the ids that report was built from.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, int>> ResolveErpItemIdsAsync(CancellationToken ct)
    {
        var erpPrices = await _erpReader.GetAllAsync(forceReload: false, ct);

        var byCode = erpPrices
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Taking the first of several ceník rows for one code is the same choice
        // SetProductPriceHandler makes, but a sync makes it for up to a whole selection at once
        // rather than for one product under an operator's eye. Query 41's row shape is not
        // something we can inspect, so say so once per run if it ever turns out to hold more
        // than one price list: a silent wrong-ceník write across a bulk sync is not something to
        // discover from the prices alone.
        var ambiguous = byCode.Where(g => g.Count() > 1).ToList();
        if (ambiguous.Count > 0)
        {
            _logger.LogWarning(
                "Price sync found {AmbiguousCount} product codes with more than one Flexi ceník row " +
                "(for example {ExampleCodes}); each was written to the first row seen, which may not " +
                "be the intended price list.",
                ambiguous.Count,
                string.Join(", ", ambiguous.Take(5).Select(g => g.Key)));
        }

        return byCode.ToDictionary(g => g.Key, g => g.First().ErpItemId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<(int Written, int Failed, int Remaining)> WriteAllAsync(
        IReadOnlyList<PriceDivergenceRowDto> targets,
        IReadOnlyDictionary<string, int> erpItemIds,
        CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow() + WriteBudget;
        var written = 0;
        var failed = 0;

        foreach (var row in targets)
        {
            ct.ThrowIfCancellationRequested();

            var budgetLeft = deadline - _timeProvider.GetUtcNow();
            if (budgetLeft <= TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "Price sync stopped after {Written} writes: the {Budget} write budget is spent. " +
                    "{Remaining} rows are left for the next run.",
                    written, WriteBudget, targets.Count - written - failed);
                break;
            }

            if (await TryWriteAsync(row, erpItemIds, budgetLeft, ct))
            {
                written++;
            }
            else
            {
                failed++;
            }
        }

        return (written, failed, targets.Count - written - failed);
    }

    /// <summary>
    /// The rows as they stand after the writes. Re-read rather than assumed: Flexi stores the
    /// base price and reconstructs the with-VAT figure on read, so what the ERP now holds is a
    /// fact to be read back, not one to be predicted from what was sent. A row whose write
    /// failed truthfully comes back still divergent.
    ///
    /// A failure here must not become the operator's whole story: prices have already been
    /// written, and propagating would report a sync that did work as one that did nothing. The
    /// pre-write rows are returned instead, alongside counts that are true either way.
    /// </summary>
    private async Task<List<PriceDivergenceRowDto>> ReadBackAsync(
        IReadOnlyCollection<string> productCodes, PriceComparisonResult beforeWrites, int written, CancellationToken ct)
    {
        if (written == 0)
        {
            return beforeWrites.Rows;
        }

        try
        {
            return (await _comparisonService.BuildScopedReportAsync(productCodes, ct)).Rows;
        }
        // Only the caller walking away propagates. Testing the exception type instead would let a
        // read timeout through: an HttpClient that gives up throws TaskCanceledException — an
        // OperationCanceledException — with this request's own token never cancelled, and the
        // writes above would then be reported as a total failure.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Price sync wrote {Written} prices into Flexi but could not re-read the " +
                    "comparison; the rows reported are the ones from before the writes.", written);
            return beforeWrites.Rows;
        }
    }

    /// <summary>
    /// <paramref name="budgetLeft"/> caps this one write, not just the loop around it. Flexi's
    /// own client waits five minutes for a reply, so checking the budget only between rows
    /// bounds nothing: a single slow write sails past the gateway's 230 s and hands the operator
    /// a failed request for prices that did reach the ERP — the very outcome the budget exists
    /// to prevent. Abandoning the call instead keeps the run inside its budget and lets the
    /// response account for the row.
    /// </summary>
    private async Task<bool> TryWriteAsync(
        PriceDivergenceRowDto row,
        IReadOnlyDictionary<string, int> erpItemIds,
        TimeSpan budgetLeft,
        CancellationToken ct)
    {
        var code = row.ProductCode ?? string.Empty;
        var priceWithVat = row.ShoptetPriceWithVat!.Value;

        if (!erpItemIds.TryGetValue(code, out var erpItemId) || erpItemId <= 0)
        {
            // No change log row: nothing was attempted, and this is a standing fact about the
            // product rather than an event, so logging it per click would fill the history with
            // the same row over and over.
            _logger.LogWarning(
                "Price sync did not write {ProductCode} into Flexi: no ceník id, so a write " +
                "would create a new price list item.", code);
            return false;
        }

        using var budgetCts = new CancellationTokenSource(budgetLeft, _timeProvider);
        using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(ct, budgetCts.Token);

        try
        {
            await _erpWriter.SetPriceWithVatAsync(erpItemId, priceWithVat, writeCts.Token);
        }
        // A cancelled request is the operator (or the gateway) walking away: it must abort the
        // run rather than be recorded as this row's write having failed — the write may well
        // have landed. A Flexi-side timeout leaves the request's own token uncancelled and is
        // still a genuine write failure.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        // The budget ran out mid-write. Counted as failed rather than remaining, because unlike
        // a row never attempted this one may well have landed in the ERP — abandoning the call
        // says nothing about what Flexi did with it. The next run's comparison settles it: if it
        // landed the row comes back in agreement and is skipped, and if it did not it is retried.
        catch (OperationCanceledException) when (budgetCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Price sync abandoned the write of {ProductCode} into Flexi ceník {ErpItemId}: the " +
                "write budget ran out while it was in flight. Whether it landed is unknown; the " +
                "next sync will settle it.", code, erpItemId);
            await AppendAsync(row, flexiSucceeded: false, "Write budget spent while the write was in flight; outcome unknown.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Price sync failed to write {ProductCode} into Flexi ceník {ErpItemId}.", code, erpItemId);
            await AppendAsync(row, flexiSucceeded: false, ex.Message);
            return false;
        }

        await AppendAsync(row, flexiSucceeded: true, error: null);
        return true;
    }

    /// <summary>
    /// Records the ERP write in the same append-only history as an operator's own price edit.
    /// Only an attempted write is recorded. A sync writes Flexi alone, so the columns read:
    /// <c>OldPriceWithVat</c> is what Flexi held before, <c>NewPriceWithVat</c> is the Shoptet
    /// price propagated into it, and <c>ShoptetSucceeded</c> is true because Shoptet already
    /// holds that price and was left untouched — never because anything was written there.
    ///
    /// Swallows its own failure for the same reason <c>SetProductPriceHandler</c> does: by the
    /// time this runs the live ERP write has already landed, and reporting a completed price
    /// change as an error would invite a pointless retry. Always appends with
    /// <see cref="CancellationToken.None"/> so a cancelled request cannot lose the only record
    /// of a write that did happen.
    /// </summary>
    private async Task AppendAsync(PriceDivergenceRowDto row, bool flexiSucceeded, string? error)
    {
        try
        {
            var currentUser = _currentUserService.GetCurrentUser();

            await _changeLog.AppendAsync(new ProductPriceChangeLog
            {
                ProductCode = row.ProductCode ?? string.Empty,
                OldPriceWithVat = row.FlexiPriceWithVat,
                NewPriceWithVat = row.ShoptetPriceWithVat ?? 0m,
                ChangedAt = _timeProvider.GetUtcNow().UtcDateTime,
                ChangedBy = currentUser.Email ?? currentUser.Name ?? currentUser.Id ?? "unknown",
                ShoptetSucceeded = true,
                FlexiSucceeded = flexiSucceeded,
                ErrorMessage = error,
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to append the price sync log row for {ProductCode} (flexi={FlexiOk}); " +
                    "the ERP write itself is unaffected.", row.ProductCode, flexiSucceeded);
        }
    }
}
