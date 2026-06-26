using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;

public class GetMeetingUsersResponse : BaseResponse
{
    public GetMeetingUsersResponse() { }
    public GetMeetingUsersResponse(ErrorCodes errorCode) : base(errorCode) { }

    public List<MeetingUserDto> Users { get; set; } = new();
}
