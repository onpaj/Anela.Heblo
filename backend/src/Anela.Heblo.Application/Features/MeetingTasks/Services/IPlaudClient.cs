namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<PlaudSummaryResult> GetSummaryAsync(string recordingId, CancellationToken ct = default);
    Task<PlaudFileDetail> GetFileDetailAsync(string recordingId, CancellationToken ct = default);
}
