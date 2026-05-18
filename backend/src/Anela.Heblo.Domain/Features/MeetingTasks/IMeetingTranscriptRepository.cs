namespace Anela.Heblo.Domain.Features.MeetingTasks;

public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        bool isManager,
        string? userEmail,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<bool> ExistsByPlaudIdAsync(string plaudRecordingId, CancellationToken ct = default);

    Task AddAsync(MeetingTranscript transcript, CancellationToken ct = default);

    Task SetAccessAsync(
        MeetingTranscript transcript,
        MeetingAccessLevel level,
        IReadOnlyList<MeetingAccessGrant> newGrants,
        CancellationToken ct = default);

    /// <summary>
    /// Removes all Pending tasks from the transcript and replaces them with
    /// <paramref name="newTasks"/>. Approved and Rejected tasks are preserved.
    /// </summary>
    Task ReplacePendingTasksAsync(
        MeetingTranscript transcript,
        IReadOnlyList<ProposedTask> newTasks,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
