namespace Anela.Heblo.Domain.Features.MeetingTasks;

public class ProposedTask
{
    public Guid Id { get; set; }
    public Guid MeetingTranscriptId { get; set; }
    public MeetingTranscript MeetingTranscript { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Assignee { get; set; } = null!;
    public string? AssigneeEmail { get; set; }
    public DateTime? DueDate { get; set; }
    public ProposedTaskStatus Status { get; set; }
    public string? ExternalTaskId { get; set; }
    public bool IsManuallyAdded { get; set; }
}
