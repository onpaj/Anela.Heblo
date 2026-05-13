using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskRequest : IRequest<AddProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public DateTime? DueDate { get; set; }
}
