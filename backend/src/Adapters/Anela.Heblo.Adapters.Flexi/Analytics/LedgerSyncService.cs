using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class LedgerSyncService : IEntitySyncService
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

        try
        {
            var skip = 0;
            while (true)
            {
                // SDK 0.1.136: GetChangedSinceAsync(since, limit?, skip?, ct)
                var batch = await _ledgerClient.GetChangedSinceAsync(
                    changedSince, _options.BatchSize, skip, ct);

                if (batch.Count == 0)
                    break;

                var entries = batch.Select(Map).ToList();
                var upserted = await UpsertBatchAsync(entries, ct);
                totalFetched += batch.Count;
                totalUpserted += upserted;
                skip += batch.Count;

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
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex, "FlexiAnalyticsSync.EntityFailed {EntityName}", EntityName);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK");
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

    private static LedgerEntry Map(LedgerItemFlexiDto dto) => new()
    {
        FlexiId = dto.Id,
        Code = dto.ParSymbol,
        EntryDate = DateOnly.FromDateTime(dto.AccountingDate),
        AccountDebit = dto.DebitAccountShowAs,
        AccountCredit = dto.CreditAccountShowAs,
        Amount = (decimal)dto.AmountLocal,
        Currency = dto.CurrencyRef,
        CostCenter = dto.DepartmentRef,
        Description = dto.Description,
        LastModified = dto.LastUpdate?.ToUniversalTime(),
        // Period, DocumentType, Contact, AccountingTemplate: SDK 0.1.136 does not expose these fields yet;
        // wire them when the SDK adds PeriodRef / DocumentTypeRef / ContactRef / AccountingTemplateRef.
        RawPayload = SerializeRaw(dto),
        SyncedAt = DateTimeOffset.UtcNow,
    };

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
