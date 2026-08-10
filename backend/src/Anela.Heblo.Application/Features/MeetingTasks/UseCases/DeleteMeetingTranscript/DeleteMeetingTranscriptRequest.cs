using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;

public class DeleteMeetingTranscriptRequest : IRequest<DeleteMeetingTranscriptResponse>
{
    public Guid TranscriptId { get; set; }
}
