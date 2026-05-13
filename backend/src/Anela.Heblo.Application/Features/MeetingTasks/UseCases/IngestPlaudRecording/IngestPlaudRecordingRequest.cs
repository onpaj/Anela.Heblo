using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingRequest : IRequest<IngestPlaudRecordingResponse>
{
    public string PlaudRecordingId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime PlaudCreatedAt { get; set; }
}
