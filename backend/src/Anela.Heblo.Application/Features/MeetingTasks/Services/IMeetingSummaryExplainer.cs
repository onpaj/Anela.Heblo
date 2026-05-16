namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public class MeetingSummaryExplanation
{
    public string RelevantTranscript { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public interface IMeetingSummaryExplainer
{
    Task<MeetingSummaryExplanation> ExplainAsync(
        string transcript,
        string selectedText,
        CancellationToken ct = default);
}
