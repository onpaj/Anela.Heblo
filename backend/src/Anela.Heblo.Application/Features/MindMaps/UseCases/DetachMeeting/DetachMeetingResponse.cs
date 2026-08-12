using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;

public class DetachMeetingResponse : BaseResponse
{
    public DetachMeetingResponse() { }
    public DetachMeetingResponse(ErrorCodes errorCode) : base(errorCode) { }
}
