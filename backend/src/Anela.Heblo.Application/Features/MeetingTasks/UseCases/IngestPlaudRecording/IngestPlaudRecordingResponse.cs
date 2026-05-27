using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingResponse : BaseResponse
{
    public bool Skipped { get; set; }
    public bool NotGenerated { get; set; }
    public Guid? TranscriptId { get; set; }
}
