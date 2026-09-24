using System.Globalization;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class LedgerSyncService : IEntitySyncService, ILedgerBackfillService
{
    private const string EntityName = "ledger_entry";

    private readonly ILedgerClient _ledgerClient;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<LedgerSyncService> _logger;

    public LedgerSyncService(
        ILedgerClient ledgerClient,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<LedgerSyncService> logger)
    {
        _ledgerClient = ledgerClient;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);

        // Use watermark − 1h to absorb clock skew / slow Flexi updates.
        // First run (null watermark): fall back to InitialBackfillFrom.
        var changedSince = state.Watermark.HasValue
            ? state.Watermark.Value.AddHours(-1).UtcDateTime
            : _options.GetInitialBackfillDateTime();

        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation(
            "FlexiAnalyticsSync.EntityStarted {EntityName} watermark={Watermark} changedSince={ChangedSince}",
            EntityName, state.Watermark, changedSince);

        var totalFetched = 0;
        var totalUpserted = 0;
        // Highest LastModified actually written, so a run that cannot finish still makes forward
        // progress. Safe because GetChangedSinceAsync orders by lastUpdate ascending -- see the
        // failure branch for the evidence and for how tied timestamps are handled.
        DateTimeOffset? ingestedUpTo = null;

        try
        {
            var skip = 0;
            while (true)
            {
                // SDK 0.1.141: GetChangedSinceAsync(since, limit?, skip?, ct)
                var batch = await _ledgerClient.GetChangedSinceAsync(
                    changedSince, _options.BatchSize, skip, ct);

                if (batch.Count == 0)
                    break;

                var entries = batch.Select(Map).ToList();
                var upserted = await UpsertBatchAsync(entries, ct);
                totalFetched += batch.Count;
                totalUpserted += upserted;
                skip += batch.Count;

                // `>` against a null nullable is always false, so the first batch needs the
                // explicit HasValue check or the high-water mark never leaves null.
                var batchHighWater = entries.Max(e => e.LastModified);
                if (batchHighWater.HasValue && (!ingestedUpTo.HasValue || batchHighWater > ingestedUpTo))
                    ingestedUpTo = batchHighWater;

                if (batch.Count < _options.BatchSize)
                    break;
            }

            state.Watermark = DateTimeOffset.UtcNow;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.EntityCompleted {EntityName} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted}",
                EntityName, totalFetched, totalUpserted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A caller-requested stop (Ctrl+C in the backfill tool, host shutdown) is not a sync
            // failure -- recording it as FAILED is indistinguishable from a real Flexi outage to
            // anyone reading sync_state. Filter on the token, not on the exception type: an HTTP
            // timeout also surfaces as TaskCanceledException, and that one IS a failure.
            state.LastRunStatus = "CANCELLED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;

            // Cancellation is the expected end of an oversized run (the job cancels after
            // RequestTimeoutSeconds), so this is precisely the case the high-water mark exists for.
            if (ingestedUpTo.HasValue && (!state.Watermark.HasValue || ingestedUpTo > state.Watermark))
            {
                state.Watermark = ingestedUpTo;
            }

            _logger.LogWarning(
                "FlexiAnalyticsSync.EntityCancelled {EntityName} rowsUpserted={RowsUpserted}",
                EntityName, totalUpserted);

            await SaveStateAsync(state);
            throw;
        }
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            // Keep the high-water mark so a delta too large to finish in one run converges over
            // several instead of restarting from InitialBackfillFrom every night.
            //
            // This IS sound, because the pages arrive ordered. LedgerRequest's (DateTime since)
            // constructor sets Order = "lastUpdate" alongside the filter -- in the 0.1.141 binary
            // that is `IL_0078: ldstr "lastUpdate"` / `call set_Order`, and in source it is the
            // line right after `Filter = ...` in LedgerRequest.cs. So every row with a lastUpdate
            // below the high-water mark has already been ingested.
            //
            // Ties are the real hazard -- FlexiBee bulk-touches the ledger, and 288k rows share
            // lastUpdate = 2025-06-03 -- but `order` has no tiebreaker, so a tie group may split
            // across a page boundary. The -1h overlap on the next run covers that: re-querying
            // from (mark - 1h) re-fetches the entire tie group, and the upsert is idempotent.
            //
            // Never moves the watermark backwards.
            if (ingestedUpTo.HasValue && (!state.Watermark.HasValue || ingestedUpTo > state.Watermark))
            {
                state.Watermark = ingestedUpTo;
            }

            _logger.LogError(ex,
                "FlexiAnalyticsSync.EntityFailed {EntityName} rowsUpserted={RowsUpserted} ingestedUpTo={IngestedUpTo}",
                EntityName, totalUpserted, ingestedUpTo);
        }

        await SaveStateAsync(state);
        return new SyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK");
    }

    /// <inheritdoc />
    public async Task<SyncResult> BackfillAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Without this an inverted range yields zero windows, ingests nothing, and still reports
        // OK -- which used to hand the nightly incremental a watermark covering a load that never
        // happened.
        if (from > to)
            throw new ArgumentException($"Backfill range ends before it starts: from={from:O} to={to:O}.", nameof(from));

        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);
        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "BACKFILL";
        await _watermarkRepo.SaveAsync(state, ct);

        var startedAt = DateTimeOffset.UtcNow;
        var totalFetched = 0;
        var totalUpserted = 0;

        try
        {
            foreach (var (windowFrom, windowTo) in MonthWindows(from, to))
            {
                var windowFetched = 0;
                var skip = 0;

                while (true)
                {
                    var batch = await _ledgerClient.GetAsync(
                        windowFrom.ToDateTime(TimeOnly.MinValue),
                        windowTo.ToDateTime(TimeOnly.MinValue),
                        debitAccountPrefixes: null,
                        creditAccountPrefix: null,
                        departmentId: null,
                        limit: _options.BatchSize,
                        skip: skip,
                        cancellationToken: ct);

                    if (batch.Count == 0)
                        break;

                    totalUpserted += await UpsertBatchAsync(batch.Select(Map).ToList(), ct);
                    windowFetched += batch.Count;
                    skip += batch.Count;

                    if (batch.Count < _options.BatchSize)
                        break;

                    if (_options.BackfillThrottleMilliseconds > 0)
                        await Task.Delay(_options.BackfillThrottleMilliseconds, ct);
                }

                totalFetched += windowFetched;

                // Progress is persisted per window so an interrupted backfill can be restarted
                // from the month it died on rather than from the beginning.
                state.LastRunRowsFetched = totalFetched;
                state.LastRunRowsUpserted = totalUpserted;
                await _watermarkRepo.SaveAsync(state, ct);

                _logger.LogInformation(
                    "FlexiAnalyticsSync.BackfillWindow {EntityName} from={WindowFrom} to={WindowTo} rows={WindowRows} totalRows={TotalRows}",
                    EntityName, windowFrom, windowTo, windowFetched, totalFetched);
            }

            // Hand over to the nightly incremental ONLY when this range reached today. The nightly
            // path filters on `lastUpdate gte watermark`, so a watermark of "now" asserts that
            // everything posted to date is on disk. Stamping it after a partial range (a single
            // month, a single year) would silently strand every un-edited row between `to` and now:
            // the delta would never look that far back again and the run would still read OK.
            // Leaving it untouched costs a wider first delta, which upserts idempotently.
            // Zero rows over a whole backfill range is not a legitimate outcome -- you do not run a
            // backfill over a period you expect to be empty. It is what a rejected WQL filter or
            // expired auth looks like, because the SDK client logs the error and returns an empty
            // list rather than throwing. Reporting OK here would stamp the watermark and strand
            // the entire history behind a green sync_state.
            if (totalFetched == 0)
                throw new InvalidOperationException(
                    $"Backfill of {EntityName} over {from:O}..{to:O} returned no rows at all. " +
                    "The range is not empty in FlexiBee, so this is almost certainly a rejected query or expired credentials.");

            var reachesToday = to >= DateOnly.FromDateTime(startedAt.UtcDateTime);
            if (reachesToday && (!state.Watermark.HasValue || startedAt > state.Watermark))
            {
                // Taken from when the backfill STARTED, not when it finished, so rows edited while
                // it was running are re-read by the next delta instead of being missed.
                state.Watermark = startedAt;
            }
            else if (!reachesToday)
            {
                _logger.LogInformation(
                    "FlexiAnalyticsSync.BackfillPartialRange {EntityName} to={To} watermark left at {Watermark}; the nightly delta still covers the gap",
                    EntityName, to, state.Watermark);
            }

            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.BackfillCompleted {EntityName} from={From} to={To} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted}",
                EntityName, from, to, totalFetched, totalUpserted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A caller-requested stop (Ctrl+C in the backfill tool, host shutdown) is not a sync
            // failure -- recording it as FAILED is indistinguishable from a real Flexi outage to
            // anyone reading sync_state. Filter on the token, not on the exception type: an HTTP
            // timeout also surfaces as TaskCanceledException, and that one IS a failure.
            state.LastRunStatus = "CANCELLED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;

            _logger.LogWarning(
                "FlexiAnalyticsSync.BackfillCancelled {EntityName} rowsUpserted={RowsUpserted}",
                EntityName, totalUpserted);

            await SaveStateAsync(state);
            throw;
        }
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex,
                "FlexiAnalyticsSync.BackfillFailed {EntityName} rowsUpserted={RowsUpserted}",
                EntityName, totalUpserted);
        }

        await SaveStateAsync(state);
        return new SyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK");
    }

    /// <summary>
    /// Persists the run's outcome, detaching anything the run left on the change tracker first.
    ///
    /// A batch that threw leaves its entities Added-but-unsaved. Writing sync_state through the
    /// same context then flushes those orphans too, and on 2026-09-24 that turned a recoverable
    /// page error into a duplicate-key failure, so the FAILED status was never written at all —
    /// sync_state sat at RUNNING for six hours saying nothing. Recording why a run failed must not
    /// depend on the run having succeeded.
    ///
    /// Always CancellationToken.None: on the cancellation path `ct` is already cancelled, and
    /// saving through it would throw for that reason alone.
    /// </summary>
    private async Task SaveStateAsync(SyncState state)
    {
        _dbContext.ChangeTracker.Clear();
        await _watermarkRepo.SaveAsync(state, CancellationToken.None);
    }

    /// <summary>
    /// Splits an inclusive date range into calendar-month windows, clipped to the range ends.
    /// </summary>
    internal static IEnumerable<(DateOnly From, DateOnly To)> MonthWindows(DateOnly from, DateOnly to)
    {
        var cursor = new DateOnly(from.Year, from.Month, 1);
        while (cursor <= to)
        {
            var windowEnd = cursor.AddMonths(1).AddDays(-1);
            yield return (cursor > from ? cursor : from, windowEnd < to ? windowEnd : to);
            cursor = cursor.AddMonths(1);
        }
    }

    private async Task<int> UpsertBatchAsync(List<LedgerEntry> incoming, CancellationToken ct)
    {
        var fetched = incoming.Count;

        // FlexiBee orders the changed-since query by lastUpdate with no tiebreaker, and its bulk
        // operations leave blocks of hundreds of thousands of rows sharing a single timestamp, so
        // skip-based paging can legitimately return the same row twice. Two entity instances with
        // one key is an immediate "already being tracked" throw out of DbSet.Add — which is how
        // the first live run died on 2026-09-24. Last one wins; within a single fetch the copies
        // carry identical content anyway.
        incoming = incoming
            .GroupBy(x => x.FlexiId)
            .Select(g => g.Last())
            .ToList();

        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.LedgerEntries
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var entry in incoming)
        {
            if (existing.TryGetValue(entry.FlexiId, out var existingEntry))
            {
                existingEntry.EntryDate = entry.EntryDate;
                existingEntry.Code = entry.Code;
                existingEntry.AccountDebit = entry.AccountDebit;
                existingEntry.AccountCredit = entry.AccountCredit;
                existingEntry.Amount = entry.Amount;
                existingEntry.Currency = entry.Currency;
                existingEntry.CostCenter = entry.CostCenter;
                existingEntry.Period = entry.Period;
                existingEntry.DocumentType = entry.DocumentType;
                existingEntry.Contact = entry.Contact;
                existingEntry.AccountingTemplate = entry.AccountingTemplate;
                existingEntry.Description = entry.Description;
                existingEntry.LastModified = entry.LastModified;
                existingEntry.RawPayload = entry.RawPayload;
                existingEntry.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.LedgerEntries.Add(entry);
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        // Nothing from this batch outlives it. Two reasons, both of which bit us: a row saved on
        // one page would otherwise stay tracked and collide when a later page returns it again,
        // and the ~680k-row backfill would pile every entity onto one tracker, slowing
        // DetectChanges batch by batch while holding every raw_payload string in memory at once.
        _dbContext.ChangeTracker.Clear();

        return fetched;
    }

    // FlexiBee's `ucetni-denik` is a view, so every row comes back with `id` = -1 and carries its
    // real identity in `idUcetniDenik` (mapped by the SDK onto the weakly typed JournalId).
    // Mapping dto.Id would give every row the same primary key and the first batch's
    // SaveChangesAsync would fail on a duplicate key. Verified against the live company file
    // 2026-09-22.
    internal static LedgerEntry Map(LedgerItemFlexiDto dto) => new()
    {
        FlexiId = ParseJournalId(dto),
        Code = dto.ParSymbol,
        EntryDate = DateOnly.FromDateTime(dto.AccountingDate),
        // postingPeriod, e.g. "2026/06" — the accounting period the row was posted into, which is
        // not always the month of EntryDate.
        Period = dto.Period,
        // idDokl@evidencePath: faktura-prijata / banka / interni-doklad / skladovy-pohyb /
        // pokladni-pohyb. Ad spend (#31-#33) arrives as faktura-prijata.
        DocumentType = dto.DocumentIdEvidencePath,
        // The SDK asks for mdUcet(kod,nazev,id) with includes=/ucetni-denik/mdUcet, so FlexiBee
        // answers with a nested array and omits the mdUcet@showAs scalar that DebitAccountShowAs
        // binds to. Same for dalUcet, stredisko and mena. dto.DebitAccount/CreditAccount/Department
        // are List.First() wrappers that throw on an empty list, hence the lists directly.
        AccountDebit = dto.DebitAccountList?.FirstOrDefault()?.Code,
        AccountCredit = dto.CreditAccountList?.FirstOrDefault()?.Code,
        Amount = (decimal)dto.AmountLocal,
        Currency = dto.Currency?.FirstOrDefault()?.Code,
        CostCenter = dto.DepartmentList?.FirstOrDefault()?.Code,
        // firma@showAs is "CODE: Name", which groups stably; nazFirmy is the bare name and is the
        // only thing present on a handful of older rows.
        Contact = NullIfBlank(dto.ContactShowAs) ?? NullIfBlank(dto.CompanyName),
        // Always null today: `ucetni-denik` exposes 38 properties and the předkontace is not one of
        // them — it lives on the source document, not on the journal line. Kept so the column
        // populates itself if a later SDK/FlexiBee version starts returning one.
        AccountingTemplate = dto.AccountingTemplate,
        Description = dto.Description,
        // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
        // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
        LastModified = dto.LastUpdate.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value.DateTime, TimeZoneInfo.Local)
            : (DateTimeOffset?)null,
        RawPayload = SerializeRaw(dto),
        SyncedAt = DateTimeOffset.UtcNow,
    };

    private static long ParseJournalId(LedgerItemFlexiDto dto)
    {
        var raw = dto.JournalId?.ToString();
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return id;

        throw new InvalidOperationException(
            $"Ledger row has no usable idUcetniDenik (JournalId='{raw}', doklad='{dto.Document}').");
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    // LedgerItemFlexiDto has computed properties (Department, DebitAccount, CreditAccount)
    // that throw when their backing list fields are null. Use Newtonsoft with error-swallowing
    // so those problematic properties are skipped rather than aborting the whole serialization.
    private static readonly JsonSerializerSettings RawPayloadSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Error = (_, args) => args.ErrorContext.Handled = true,
    };

    private static string SerializeRaw(LedgerItemFlexiDto dto) =>
        JsonConvert.SerializeObject(dto, RawPayloadSettings);
}
