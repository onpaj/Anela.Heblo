using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoResponse : BaseResponse
{
    public SubmitToTodoResponse() { }
    public SubmitToTodoResponse(ErrorCodes errorCode) : base(errorCode) { }

    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}
