namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class PlaudRecordingSummary
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public bool HasTranscript { get; set; }
    public bool HasSummary { get; set; }
}
