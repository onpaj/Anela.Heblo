using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;

public class DetachMeetingHandler : IRequestHandler<DetachMeetingRequest, DetachMeetingResponse>
{
    private readonly IMindMapRepository _repository;

    public DetachMeetingHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<DetachMeetingResponse> Handle(DetachMeetingRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.MindMapId, cancellationToken);
        if (map is null)
        {
            return new DetachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        var link = map.Meetings.FirstOrDefault(m => m.MeetingTranscriptId == request.MeetingTranscriptId);
        if (link is null)
        {
            return new DetachMeetingResponse(ErrorCodes.ResourceNotFound);
        }

        map.Meetings.Remove(link);
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return new DetachMeetingResponse();
    }
}
