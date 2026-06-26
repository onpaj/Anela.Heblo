using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public class ReimportMeetingTranscriptRequest : IRequest<ReimportMeetingTranscriptResponse>
{
    public Guid Id { get; set; }
}
