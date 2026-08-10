using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;

/// <summary>
/// Processes all pending meetings of one mind map sequentially (chronological by
/// meeting date). Enqueued on attach and on manual regenerate. Saves after each
/// meeting so a later failure keeps earlier progress. Resumable: on requeue, already
/// processed meetings are skipped, so the default 10-attempt Hangfire retry is safe.
/// </summary>
public class MindMapUpdateJob
{
    private readonly IMindMapRepository _repository;
    private readonly IMindMapUpdater _updater;
    private readonly MindMapGuard _guard;
    private readonly ILogger<MindMapUpdateJob> _logger;

    public MindMapUpdateJob(
        IMindMapRepository repository,
        IMindMapUpdater updater,
        MindMapGuard guard,
        ILogger<MindMapUpdateJob> logger)
    {
        _repository = repository;
        _updater = updater;
        _guard = guard;
        _logger = logger;
    }

    [DisableConcurrentExecution("mindmap-update:{0}", 600)]
    [AutomaticRetry(Attempts = 10)]
    public async Task RunAsync(Guid mindMapId, CancellationToken ct)
    {
        var map = await _repository.GetByIdAsync(mindMapId, ct);
        if (map is null)
        {
            _logger.LogWarning("Mind map {MindMapId} not found — nothing to update", mindMapId);
            return;
        }

        var pending = GetPendingMeetingsChronologically(map);

        foreach (var meeting in pending)
        {
            try
            {
                await ProcessMeetingAsync(map, meeting, ct);
            }
            catch (OperationCanceledException)
            {
                // Cancellation (deploy/restart) is infrastructure, not a business failure:
                // mark Failed for visibility but rethrow so Hangfire requeues the job.
                await MarkFailedAsync(map, "Job cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Mind map {MindMapId} update failed on meeting {MeetingId}",
                    map.Id, meeting.MeetingTranscriptId);
                await MarkFailedAsync(map, ex.Message);
                return;
            }
        }

        map.Status = MindMapStatus.Idle;
        map.LastError = null;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deserializes the current document, asks the LLM for the next version, runs the
    /// deterministic guard over the reply, snapshots the PRE-update json as the new
    /// version, then writes the guarded result. Any exception here (updater or guard)
    /// happens before <see cref="MindMap.CurrentJson"/> is touched.
    /// </summary>
    private async Task ProcessMeetingAsync(MindMap map, MindMapMeeting meeting, CancellationToken ct)
    {
        var current = MindMapJson.Deserialize(map.CurrentJson);
        var llmResult = await _updater.UpdateAsync(current, meeting.MeetingTranscript, ct);
        var next = _guard.ApplyLlmUpdate(current, llmResult, meeting.MeetingTranscriptId);

        var nextVersionNumber = map.Versions.Count == 0
            ? 1
            : map.Versions.Max(v => v.VersionNumber) + 1;
        map.Versions.Add(new MindMapVersion
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            VersionNumber = nextVersionNumber,
            Json = map.CurrentJson,
            CreatedAt = DateTime.UtcNow,
            TriggerMeetingId = meeting.MeetingTranscriptId
        });

        map.CurrentJson = MindMapJson.Serialize(next);
        meeting.ProcessedAt = DateTime.UtcNow;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks the map Failed in memory and persists it through a change-tracker-free
    /// update. A tracked <see cref="IMindMapRepository.SaveChangesAsync"/> here would risk
    /// resubmitting whatever entities were left dirty by the failure that got us here (e.g.
    /// a failed save leaves its Added/Modified entities tracked) — see repository docs.
    /// The persistence attempt itself is guarded so a failure to record the failure can
    /// never escape and mask the original error.
    /// </summary>
    private async Task MarkFailedAsync(MindMap map, string lastError)
    {
        map.Status = MindMapStatus.Failed;
        map.LastError = lastError;
        map.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _repository.SetFailedAsync(map.Id, lastError, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mind map {MindMapId} failed to persist Failed status", map.Id);
        }
    }

    private List<MindMapMeeting> GetPendingMeetingsChronologically(MindMap map)
    {
        var pending = map.Meetings.Where(m => m.ProcessedAt == null).ToList();

        foreach (var meeting in pending.Where(m => m.MeetingTranscript == null))
        {
            _logger.LogWarning(
                "Mind map {MindMapId} meeting {MeetingId} has no loaded transcript — skipping",
                map.Id, meeting.MeetingTranscriptId);
        }

        return pending
            .Where(m => m.MeetingTranscript != null)
            .OrderBy(m => m.MeetingTranscript.PlaudCreatedAt)
            .ToList();
    }
}
