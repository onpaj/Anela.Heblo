using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;

public class DetachMeetingRequest : IRequest<DetachMeetingResponse>
{
    public Guid MindMapId { get; set; }
    public Guid MeetingTranscriptId { get; set; }
}
