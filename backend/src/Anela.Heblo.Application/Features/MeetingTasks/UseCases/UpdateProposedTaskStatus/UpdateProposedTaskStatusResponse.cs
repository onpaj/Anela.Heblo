using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusResponse : BaseResponse
{
    public UpdateProposedTaskStatusResponse() { }
    public UpdateProposedTaskStatusResponse(ErrorCodes errorCode) : base(errorCode) { }
}
