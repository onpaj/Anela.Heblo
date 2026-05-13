using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskResponse : BaseResponse
{
    public AddProposedTaskResponse() { }
    public AddProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }

    public Guid TaskId { get; set; }
}
