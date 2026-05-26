using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Model.Accounting.AccountingTemplates;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class AccountingTemplateSyncService : IEntitySyncService
{
    private const string EntityName = "accounting_template";

    private readonly IAccountingTemplateClient _accountingTemplateClient;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly ILogger<AccountingTemplateSyncService> _logger;

    public AccountingTemplateSyncService(
        IAccountingTemplateClient accountingTemplateClient,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        ILogger<AccountingTemplateSyncService> logger)
    {
        _accountingTemplateClient = accountingTemplateClient;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
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
            var dtos = await _accountingTemplateClient.GetAsync(ct);
            var templates = dtos.Select(Map).ToList();
            totalFetched = templates.Count;
            totalUpserted = await UpsertAllAsync(templates, ct);

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

    private async Task<int> UpsertAllAsync(List<AccountingTemplate> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.AccountingTemplates
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var template in incoming)
        {
            if (existing.TryGetValue(template.FlexiId, out var existingTemplate))
            {
                existingTemplate.Code = template.Code;
                existingTemplate.Name = template.Name;
                existingTemplate.Description = template.Description;
                existingTemplate.LastModified = template.LastModified;
                existingTemplate.RawPayload = template.RawPayload;
                existingTemplate.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.AccountingTemplates.Add(template);
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

    private static AccountingTemplate Map(AccountingTemplateFlexiDto dto) => new()
    {
        FlexiId = (long)dto.Id,
        Code = dto.Code ?? "",
        Name = dto.Name,
        Description = dto.Description,
        LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
        RawPayload = JsonConvert.SerializeObject(dto, RawPayloadSettings),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
