using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;

public class UpdateMeetingAccessResponse : BaseResponse
{
    public string? AccessLevel { get; set; }
    public List<MeetingAccessGrantDto> Grants { get; set; } = new();

    public UpdateMeetingAccessResponse() { }
    public UpdateMeetingAccessResponse(ErrorCodes errorCode) : base(errorCode) { }
    public UpdateMeetingAccessResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
