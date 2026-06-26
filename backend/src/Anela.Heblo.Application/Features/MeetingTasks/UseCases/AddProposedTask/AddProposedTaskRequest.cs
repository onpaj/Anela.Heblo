using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskRequest : IRequest<AddProposedTaskResponse>
{
    public Guid TranscriptId { get; set; }

    [Required]
    public string Title { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    [Required]
    public string Assignee { get; set; } = null!;

    public string? AssigneeEmail { get; set; }

    public DateTime? DueDate { get; set; }
}
