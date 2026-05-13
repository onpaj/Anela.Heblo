using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskRequest : IRequest<UpdateProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
}
