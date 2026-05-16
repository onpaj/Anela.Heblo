using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoRequest : IRequest<SubmitToTodoResponse>
{
    public Guid TranscriptId { get; set; }
}
