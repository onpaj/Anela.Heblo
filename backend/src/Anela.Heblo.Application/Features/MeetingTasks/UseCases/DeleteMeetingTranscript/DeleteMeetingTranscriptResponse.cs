using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;

public class DeleteMeetingTranscriptResponse : BaseResponse
{
    public DeleteMeetingTranscriptResponse() { }

    public DeleteMeetingTranscriptResponse(ErrorCodes errorCode) : base(errorCode) { }
}
