using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateTranscriptStatus;

public class UpdateTranscriptStatusRequest : IRequest<UpdateTranscriptStatusResponse>
{
    public Guid TranscriptId { get; set; }
    public string Status { get; set; } = null!;
}
