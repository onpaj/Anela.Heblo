using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Ecomail.Services;

public class EcomailSyncService : IEcomailSyncService
{
    /// <summary>stats-detail events worth storing per month. There is no conversion event.</summary>
    private static readonly string[] MonthlyEvents = { "send", "open", "click", "unsub" };

    private readonly IEcomailApiClient _api;
    private readonly IEcomailRepository _repository;
    private readonly EcomailOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EcomailSyncService> _logger;

    public EcomailSyncService(
        IEcomailApiClient api,
        IEcomailRepository repository,
        IOptions<EcomailOptions> options,
        TimeProvider timeProvider,
        ILogger<EcomailSyncService> logger)
    {
        _api = api;
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<EcomailSyncReport> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var now = _timeProvider.GetUtcNow().DateTime;
        var today = DateOnly.FromDateTime(now);

        var (campaigns, campaignStatsFetched) = await SyncCampaignsAsync(now, errors, cancellationToken);
        var (pipelineIds, pipelines) = await SyncPipelinesAsync(now, errors, cancellationToken);
        var snapshots = await SyncSnapshotsAsync(pipelineIds, today, errors, cancellationToken);
        var months = await SyncAutomationMonthsAsync(pipelineIds, today, now, errors, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        return new EcomailSyncReport(campaigns, pipelines, snapshots, months, campaignStatsFetched, errors);
    }

    private async Task<(int Count, int StatsFetched)> SyncCampaignsAsync(DateTime now, List<string> errors, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetCampaignsByIdAsync(cancellationToken);
        var count = 0;
        var statsFetched = 0;

        IReadOnlyList<EcomailCampaignDto> remote;
        try
        {
            remote = await _api.GetCampaignsAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A transient failure listing campaigns must not abort the run: pipelines and
            // snapshots still need to run, because a snapshot day missed here can never be
            // backfilled (Ecomail exposes lifetime automation counters only).
            _logger.LogWarning(ex, "Ecomail campaigns listing failed");
            errors.Add($"campaigns: {ex.Message}");
            return (count, statsFetched);
        }

        foreach (var dto in remote)
        {
            if (!existing.TryGetValue(dto.Id, out var entity))
            {
                entity = new EcomailCampaign { Id = dto.Id };
                _repository.AddCampaign(entity);
            }

            entity.Title = dto.Title;
            entity.Subject = dto.Subject;
            entity.FromEmail = dto.FromEmail;
            entity.CampaignType = dto.CampaignType;
            entity.Status = dto.Status;
            entity.SentAt = dto.SentAt;
            entity.ParentId = dto.ParentId;
            entity.Recipients = dto.Recipients;
            entity.SyncedAt = now;
            count++;

            // Stats cost one call each. Variations are subsets of their parent, drafts have no
            // stats, SMS is not a newsletter — none of them are worth a call.
            if (!entity.IsReportable)
            {
                continue;
            }

            try
            {
                var stats = await _api.GetCampaignStatsAsync(dto.Id, cancellationToken);
                if (stats is not null)
                {
                    ApplyStats(entity, stats);
                    statsFetched++;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Filter on the token, not the exception type: a TaskCanceledException from an
                // HTTP timeout is an OperationCanceledException and must be caught here.
                _logger.LogWarning(ex, "Ecomail campaign {CampaignId} stats failed", dto.Id);
                errors.Add($"campaign {dto.Id}: {ex.Message}");
            }
        }

        return (count, statsFetched);
    }

    private static void ApplyStats(EcomailCampaign entity, EcomailStatsDto stats)
    {
        entity.Inject = stats.Inject;
        entity.Delivery = stats.Delivery;
        entity.Open = stats.Open;
        entity.TotalOpen = stats.TotalOpen;
        entity.Click = stats.Click;
        entity.TotalClick = stats.TotalClick;
        entity.Unsub = stats.Unsub;
        entity.Bounce = stats.Bounce;
        entity.Spam = stats.Spam;
        entity.Conversions = stats.Conversions;
        entity.ConversionsValue = stats.ConversionsValue;
    }

    private async Task<(List<int> Ids, int Count)> SyncPipelinesAsync(
        DateTime now, List<string> errors, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetPipelinesByIdAsync(cancellationToken);
        var ids = new List<int>();
        var count = 0;

        try
        {
            foreach (var dto in await _api.GetPipelinesAsync(cancellationToken))
            {
                if (!existing.TryGetValue(dto.Id, out var entity))
                {
                    entity = new EcomailPipeline { Id = dto.Id };
                    _repository.AddPipeline(entity);
                }

                entity.Name = dto.Name;
                entity.ListId = dto.ListId;
                entity.CreatedAt = dto.CreatedAt;
                entity.UpdatedAt = dto.UpdatedAt;
                entity.SyncedAt = now;

                ids.Add(dto.Id);
                count++;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ecomail pipelines listing failed");
            errors.Add($"pipelines: {ex.Message}");

            // The listing call is only how we discover NEW pipelines — every pipeline seen before
            // is already sitting in `existing`, loaded from our own database. Falling back to those
            // ids keeps snapshots (and automation months) running on a failed listing, because
            // Ecomail exposes lifetime counters only and a missed snapshot day can never be
            // recovered.
            ids = existing.Keys.ToList();
        }

        return (ids, count);
    }

    private async Task<int> SyncSnapshotsAsync(
        List<int> pipelineIds, DateOnly today, List<string> errors, CancellationToken cancellationToken)
    {
        var written = 0;

        foreach (var pipelineId in pipelineIds)
        {
            try
            {
                // The job runs every 6h; only the first run of a day writes a row.
                if (await _repository.SnapshotExistsAsync(pipelineId, today, cancellationToken))
                {
                    continue;
                }

                var stats = await _api.GetPipelineStatsAsync(pipelineId, cancellationToken);
                if (stats is null)
                {
                    continue;
                }

                _repository.AddSnapshot(new EcomailAutomationSnapshot
                {
                    PipelineId = pipelineId,
                    CapturedOn = today,
                    Triggered = stats.Triggered,
                    Ended = stats.Ended,
                    Send = stats.Send,
                    Open = stats.Open,
                    Click = stats.Click,
                    Unsub = stats.Unsub,
                    Bounce = stats.Bounce,
                    Conversions = stats.Conversions,
                    ConversionsValue = stats.ConversionsValue,
                });
                written++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Ecomail pipeline {PipelineId} snapshot failed", pipelineId);
                errors.Add($"pipeline {pipelineId} snapshot: {ex.Message}");
            }
        }

        return written;
    }

    private async Task<int> SyncAutomationMonthsAsync(
        List<int> pipelineIds, DateOnly today, DateTime now, List<string> errors, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetAutomationMonthsAsync(cancellationToken);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);

        // Normalise to the first of the month: AddMonths preserves day-of-month, so a
        // BackfillFrom that isn't the 1st would otherwise push both the event-count window and
        // the stored Year/Month label off the true calendar-month boundary.
        var backfillStart = new DateOnly(_options.BackfillFrom.Year, _options.BackfillFrom.Month, 1);
        var computed = 0;

        foreach (var pipelineId in pipelineIds)
        {
            for (var month = backfillStart; month <= currentMonth; month = month.AddMonths(1))
            {
                existing.TryGetValue((pipelineId, month.Year, month.Month), out var entity);

                if (entity is { IsLocked: true })
                {
                    continue;
                }

                // Months outside the recompute window are computed once, then frozen.
                var monthsBack = ((currentMonth.Year - month.Year) * 12) + currentMonth.Month - month.Month;
                var isInsideWindow = monthsBack < _options.RecomputeWindowMonths;

                if (entity is not null && !isInsideWindow)
                {
                    entity.IsLocked = true;
                    continue;
                }

                try
                {
                    var counts = new Dictionary<string, int>();
                    var to = month.AddMonths(1).AddDays(-1);

                    foreach (var eventName in MonthlyEvents)
                    {
                        counts[eventName] = await _api.GetPipelineEventCountAsync(
                            pipelineId, eventName, month, to, cancellationToken);
                    }

                    entity ??= AddAutomationMonth(pipelineId, month);

                    entity.Send = counts["send"];
                    entity.Open = counts["open"];
                    entity.Click = counts["click"];
                    entity.Unsub = counts["unsub"];
                    entity.ComputedAt = now;
                    entity.IsLocked = !isInsideWindow;
                    entity.LastError = null;
                    computed++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "Ecomail pipeline {PipelineId} month {Year}-{Month} failed", pipelineId, month.Year, month.Month);
                    errors.Add($"pipeline {pipelineId} {month:yyyy-MM}: {ex.Message}");

                    // A month that has never computed successfully has no entity yet at this point
                    // (one is only built after all four event counts succeed above), so without this
                    // it would leave no durable trace and an operator could never find it again.
                    // ComputedAt is set only on success, so LastError != null is the discriminator
                    // between "failed" and "genuinely zero events" — never infer failure from zero
                    // counts alone. IsLocked stays false so the next run retries it.
                    entity ??= AddAutomationMonth(pipelineId, month);
                    entity.LastError = ex.Message;
                }
            }
        }

        return computed;
    }

    private EcomailAutomationMonth AddAutomationMonth(int pipelineId, DateOnly month)
    {
        var entity = new EcomailAutomationMonth
        {
            PipelineId = pipelineId,
            Year = month.Year,
            Month = month.Month,
        };
        _repository.AddAutomationMonth(entity);
        return entity;
    }
}
