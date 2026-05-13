using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskResponse : BaseResponse
{
    public UpdateProposedTaskResponse() { }
    public UpdateProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }
}
