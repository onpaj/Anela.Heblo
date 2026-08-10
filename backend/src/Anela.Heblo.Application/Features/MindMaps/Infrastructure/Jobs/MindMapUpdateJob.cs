using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Application.Features.MindMaps.Services;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;

/// <summary>
/// Processes all pending meetings of one mind map sequentially (chronological by
/// meeting date). Enqueued on attach and on manual regenerate. Saves after each
/// meeting so a later failure keeps earlier progress.
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

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(Guid mindMapId, CancellationToken ct)
    {
        var map = await _repository.GetByIdAsync(mindMapId, ct);
        if (map is null)
        {
            _logger.LogWarning("Mind map {MindMapId} not found — nothing to update", mindMapId);
            return;
        }

        var pending = map.Meetings
            .Where(m => m.ProcessedAt == null && m.MeetingTranscript != null)
            .OrderBy(m => m.MeetingTranscript.PlaudCreatedAt)
            .ToList();

        foreach (var meeting in pending)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Mind map {MindMapId} update failed on meeting {MeetingId}",
                    map.Id, meeting.MeetingTranscriptId);
                map.Status = MindMapStatus.Failed;
                map.LastError = ex.Message;
                map.UpdatedAt = DateTime.UtcNow;
                await _repository.SaveChangesAsync(CancellationToken.None);
                return;
            }
        }

        map.Status = MindMapStatus.Idle;
        map.LastError = null;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(ct);
    }
}
