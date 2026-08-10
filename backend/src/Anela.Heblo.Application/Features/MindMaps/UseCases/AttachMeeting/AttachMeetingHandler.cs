using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;

public class AttachMeetingHandler : IRequestHandler<AttachMeetingRequest, AttachMeetingResponse>
{
    private readonly IMindMapRepository _mapRepository;
    private readonly IMeetingTranscriptRepository _meetingRepository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<AttachMeetingHandler> _logger;

    public AttachMeetingHandler(
        IMindMapRepository mapRepository,
        IMeetingTranscriptRepository meetingRepository,
        IMeetingAccessGuard accessGuard,
        IBackgroundJobClient backgroundJobClient,
        ILogger<AttachMeetingHandler> logger)
    {
        _mapRepository = mapRepository;
        _meetingRepository = meetingRepository;
        _accessGuard = accessGuard;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public async Task<AttachMeetingResponse> Handle(AttachMeetingRequest request, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetByIdAsync(request.MindMapId, cancellationToken);
        if (map is null)
        {
            return new AttachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        var meeting = await _meetingRepository.GetByIdAsync(request.MeetingTranscriptId, cancellationToken);
        if (meeting is null || !_accessGuard.CanAccess(meeting))
        {
            _logger.LogWarning(
                "Meeting {MeetingId} not found or not accessible for mind map {MindMapId}",
                request.MeetingTranscriptId, request.MindMapId);
            return new AttachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Meetings.Any(m => m.MeetingTranscriptId == meeting.Id))
        {
            return new AttachMeetingResponse(ErrorCodes.MindMapMeetingAlreadyAttached);
        }

        var previousStatus = map.Status;
        map.Meetings.Add(new MindMapMeeting
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            MeetingTranscriptId = meeting.Id,
            AttachedAt = DateTime.UtcNow
        });
        map.Status = MindMapStatus.Updating;
        map.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _mapRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateAttachViolation(ex))
        {
            // A concurrent request attached the same meeting first and won the unique
            // constraint race; the sequential in-memory check above cannot catch that.
            _logger.LogWarning(ex,
                "Concurrent attach detected for meeting {MeetingId} on mind map {MindMapId}",
                meeting.Id, map.Id);
            return new AttachMeetingResponse(ErrorCodes.MindMapMeetingAlreadyAttached);
        }

        try
        {
            _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // The attach itself is already persisted; without this compensation a storage
            // blip here would strand the map in Updating with nothing queued to clear it,
            // and regenerate refuses to help while Status == Updating.
            //
            // The newly attached MindMapMeeting row is deliberately NOT removed here: the
            // attach itself genuinely succeeded and is a legitimate, durable fact — the
            // meeting really is pending. Reverting only Status leaves the map in a
            // consistent "pending work, no job running" state, and Regenerate is the
            // recovery path: it scans for ProcessedAt == null regardless of Status, so once
            // Status is no longer Updating it will pick this meeting up on the next call.
            _logger.LogError(ex,
                "Failed to enqueue update job for mind map {MindMapId} after attaching meeting {MeetingId}; reverting status",
                map.Id, meeting.Id);
            map.Status = previousStatus;
            map.UpdatedAt = DateTime.UtcNow;
            try
            {
                await _mapRepository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                // Don't let a failure to persist the revert mask the original enqueue
                // failure — the caller still gets a structured error either way.
                _logger.LogError(saveEx,
                    "Failed to revert mind map {MindMapId} status after enqueue failure during attach",
                    map.Id);
            }
            return new AttachMeetingResponse(ErrorCodes.InternalServerError);
        }

        _logger.LogInformation(
            "Attached meeting {MeetingId} to mind map {MindMapId} and enqueued update",
            meeting.Id, map.Id);
        return new AttachMeetingResponse();
    }

    /// <summary>
    /// True only for the specific unique-constraint race this catch exists to handle — a
    /// connection blip or unrelated FK violation must fall through to the generic
    /// InternalServerError path instead of being misreported as "already attached".
    /// </summary>
    private static bool IsDuplicateAttachViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && string.Equals(pg.ConstraintName, "UX_MindMapMeetings_MindMapId_MeetingTranscriptId", StringComparison.Ordinal);
}
