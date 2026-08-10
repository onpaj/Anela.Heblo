using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

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

        map.Meetings.Add(new MindMapMeeting
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            MeetingTranscriptId = meeting.Id,
            AttachedAt = DateTime.UtcNow
        });
        map.Status = MindMapStatus.Updating;
        map.UpdatedAt = DateTime.UtcNow;
        await _mapRepository.SaveChangesAsync(cancellationToken);

        _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        _logger.LogInformation(
            "Attached meeting {MeetingId} to mind map {MindMapId} and enqueued update",
            meeting.Id, map.Id);
        return new AttachMeetingResponse();
    }
}
