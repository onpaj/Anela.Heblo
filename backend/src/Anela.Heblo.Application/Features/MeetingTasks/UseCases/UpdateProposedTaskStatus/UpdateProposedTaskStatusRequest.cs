using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusRequest : IRequest<UpdateProposedTaskStatusResponse>
{
    public Guid TranscriptId { get; set; }
    public Guid TaskId { get; set; }

    [Required]
    public string Status { get; set; } = null!;
}
