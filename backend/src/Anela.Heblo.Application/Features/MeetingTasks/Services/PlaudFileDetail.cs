namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed record PlaudFileDetail
{
    public bool TranscriptAvailable { get; init; }
    public bool SummaryAvailable { get; init; }
    public bool AudioAvailable { get; init; }
    public bool IsGenerated => TranscriptAvailable && SummaryAvailable;
}
