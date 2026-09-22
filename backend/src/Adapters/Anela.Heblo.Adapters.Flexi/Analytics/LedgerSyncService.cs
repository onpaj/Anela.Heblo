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
        // Highest LastModified actually written. GetChangedSinceAsync filters on `lastUpdate gte`
        // and orders by lastUpdate ascending, so on a partial run everything up to this point is
        // on disk and the next run can pick up from here instead of restarting the whole backfill.
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
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            // Keep whatever ground the run did cover, so a backfill too large for one run converges
            // over several instead of restarting from InitialBackfillFrom every night. Never moves
            // the watermark backwards.
            if (ingestedUpTo.HasValue && (!state.Watermark.HasValue || ingestedUpTo > state.Watermark))
            {
                state.Watermark = ingestedUpTo;
            }

            _logger.LogError(ex,
                "FlexiAnalyticsSync.EntityFailed {EntityName} rowsUpserted={RowsUpserted} ingestedUpTo={IngestedUpTo}",
                EntityName, totalUpserted, ingestedUpTo);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK");
    }

    /// <inheritdoc />
    public async Task<SyncResult> BackfillAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
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

            // Hand over to the nightly incremental: everything posted up to `to` is loaded, and the
            // watermark starts the next delta from when this backfill began, so anything edited
            // while it was running is picked up again.
            state.Watermark = startedAt;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.BackfillCompleted {EntityName} from={From} to={To} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted}",
                EntityName, from, to, totalFetched, totalUpserted);
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

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK");
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
        return incoming.Count;
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
