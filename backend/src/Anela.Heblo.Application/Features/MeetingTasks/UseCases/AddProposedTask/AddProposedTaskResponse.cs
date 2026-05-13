using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskResponse : BaseResponse
{
    public AddProposedTaskResponse() { }

    public AddProposedTaskResponse(ErrorCodes errorCode) : base(errorCode) { }

    public ProposedTaskDto? Task { get; set; }
}
