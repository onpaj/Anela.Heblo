using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;

public class AttachMeetingResponse : BaseResponse
{
    public AttachMeetingResponse() { }
    public AttachMeetingResponse(ErrorCodes errorCode) : base(errorCode) { }
}
