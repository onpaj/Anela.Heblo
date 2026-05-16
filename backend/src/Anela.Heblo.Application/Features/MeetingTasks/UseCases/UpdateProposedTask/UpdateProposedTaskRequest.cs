using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskRequest : IRequest<UpdateProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }

    [Required]
    public string Title { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    [Required]
    public string Assignee { get; set; } = null!;

    public string? AssigneeEmail { get; set; }

    public DateTime? DueDate { get; set; }
}
