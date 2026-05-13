namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingResponse
{
    public bool Success { get; set; } = true;
    public bool Skipped { get; set; }
    public Guid? TranscriptId { get; set; }
}
