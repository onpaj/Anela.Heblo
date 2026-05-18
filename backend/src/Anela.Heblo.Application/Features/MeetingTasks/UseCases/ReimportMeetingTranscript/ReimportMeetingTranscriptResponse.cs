using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public class ReimportMeetingTranscriptResponse : BaseResponse
{
    public ReimportMeetingTranscriptResponse() { }
    public ReimportMeetingTranscriptResponse(ErrorCodes errorCode) : base(errorCode) { }
}
