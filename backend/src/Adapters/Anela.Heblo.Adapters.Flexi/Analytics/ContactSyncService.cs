using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class ContactSyncService : IEntitySyncService
{
    private const string EntityName = "contact";

    /// <summary>
    /// ContactListRequest always renders the requested types into a <c>typVztahuK in (...)</c>
    /// clause, so an empty set yields <c>typVztahuK in ()</c> and FlexiBee rejects the whole query
    /// ("Špatný formát WQL dotazu, problém na pozici 16 poblíž textu ')'"). Naming every relation
    /// type is how you ask for the entire address book.
    /// </summary>
    private static readonly ContactType[] AllRelationTypes =
    [
        ContactType.Supplier,
        ContactType.SupplierAndCustomer,
        ContactType.Customer,
        ContactType.All,
    ];

    private readonly IContactListClient _contactListClient;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<ContactSyncService> _logger;

    public ContactSyncService(
        IContactListClient contactListClient,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<ContactSyncService> logger)
    {
        _contactListClient = contactListClient;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);

        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation(
            "FlexiAnalyticsSync.EntityStarted {EntityName}",
            EntityName);

        var totalFetched = 0;
        var totalUpserted = 0;

        try
        {
            var allDtos = new List<ContactFlexiDto>();
            var offset = 0;

            while (true)
            {
                var batch = await _contactListClient.GetAsync(
                    AllRelationTypes,
                    _options.BatchSize,
                    offset,
                    ct);

                if (batch.Count == 0)
                    break;

                allDtos.AddRange(batch);
                offset += batch.Count;

                if (batch.Count < _options.BatchSize)
                    break;
            }

            // ContactListClient logs a rejected FlexiBee response and hands back an empty list
            // instead of throwing, so without this the sync reports OK after ingesting nothing.
            // Anela's address book is never empty; an empty full refresh means the call failed.
            if (allDtos.Count == 0)
                throw new InvalidOperationException(
                    "Flexi returned no contacts. A full refresh of an address book that is never empty "
                    + "means the request was rejected — check the FlexiBee client log for the WQL error.");

            var contacts = allDtos.Select(Map).ToList();
            totalFetched = contacts.Count;
            totalUpserted = await UpsertAllAsync(contacts, ct);

            // Full-refresh entity: watermark is intentionally not written — every run fetches all rows.
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
            // A caller-requested stop is not a sync failure. Filter on the token, not on the
            // exception type: an HTTP timeout also surfaces as TaskCanceledException, and that
            // one IS a failure.
            state.LastRunStatus = "CANCELLED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;

            _logger.LogWarning("FlexiAnalyticsSync.EntityCancelled {EntityName}", EntityName);

            await _watermarkRepo.SaveAsync(state, CancellationToken.None);
            throw;
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

    private async Task<int> UpsertAllAsync(List<Contact> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.Contacts
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var contact in incoming)
        {
            if (existing.TryGetValue(contact.FlexiId, out var existingContact))
            {
                existingContact.Code = contact.Code;
                existingContact.Name = contact.Name;
                existingContact.Cin = contact.Cin;
                existingContact.Vatin = contact.Vatin;
                existingContact.LastModified = contact.LastModified;
                existingContact.RawPayload = contact.RawPayload;
                existingContact.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.Contacts.Add(contact);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return incoming.Count;
    }

    private static readonly JsonSerializerSettings RawPayloadSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Error = (_, args) => args.ErrorContext.Handled = true,
    };

    internal static Contact Map(ContactFlexiDto dto) => new()
    {
        FlexiId = dto.Id ?? throw new InvalidOperationException($"ContactFlexiDto has a null Id (Code={dto.Code})."),
        Code = dto.Code,
        Name = dto.Name,
        Cin = dto.CIN,
        Vatin = dto.VATIN,
        // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
        // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
        LastModified = dto.LastUpdate.HasValue
            ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value.DateTime, TimeZoneInfo.Local)
            : (DateTimeOffset?)null,
        RawPayload = JsonConvert.SerializeObject(dto, RawPayloadSettings),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
