namespace Anela.Heblo.Adapters.Plaud;

public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<string> GetSummaryAsync(string recordingId, CancellationToken ct = default);
}
