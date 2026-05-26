using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;
using Rem.FlexiBeeSDK.Model.Accounting.Departments;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public sealed class DepartmentSyncService
{
    private const string EntityName = "department";

    private readonly IDepartmentClient _departmentClient;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly ILogger<DepartmentSyncService> _logger;

    public DepartmentSyncService(
        IDepartmentClient departmentClient,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        ILogger<DepartmentSyncService> logger)
    {
        _departmentClient = departmentClient;
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
            var dtos = await _departmentClient.GetAsync(ct);
            var departments = dtos.Select(Map).ToList();
            totalFetched = departments.Count;
            totalUpserted = await UpsertAllAsync(departments, ct);

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

    private async Task<int> UpsertAllAsync(List<Department> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.Departments
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var dept in incoming)
        {
            if (existing.TryGetValue(dept.FlexiId, out var existingDept))
            {
                existingDept.Code = dept.Code;
                existingDept.Name = dept.Name;
                existingDept.LastModified = dept.LastModified;
                existingDept.RawPayload = dept.RawPayload;
                existingDept.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.Departments.Add(dept);
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

    private static Department Map(DepartmentFlexiDto dto) => new()
    {
        FlexiId = (long)dto.Id,
        Code = dto.Code ?? "",
        Name = dto.Name,
        LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
        RawPayload = JsonConvert.SerializeObject(dto, RawPayloadSettings),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
