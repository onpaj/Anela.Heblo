namespace Anela.Heblo.Application.Features.MeetingTasks.Contracts;

public class ProposedTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public string? AssigneeEmail { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = null!;
    public string? ExternalTaskId { get; set; }
    public bool IsManuallyAdded { get; set; }
}
