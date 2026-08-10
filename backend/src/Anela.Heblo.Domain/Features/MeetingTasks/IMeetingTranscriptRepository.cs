namespace Anela.Heblo.Domain.Features.MeetingTasks;

public interface IMeetingTranscriptRepository
{
    Task<MeetingTranscript?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(List<MeetingTranscript> Items, int TotalCount)> GetListAsync(
        MeetingTranscriptStatus? statusFilter,
        string? searchText,
        bool searchInTranscript,
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

    /// <summary>
    /// Permanently removes the transcript together with its proposed tasks and access
    /// grants (cascade), and records a <see cref="DeletedPlaudRecording"/> tombstone so
    /// the Plaud polling job does not re-ingest the recording. Saves in one transaction.
    /// </summary>
    Task DeleteAsync(MeetingTranscript transcript, string deletedByUserEmail, CancellationToken ct = default);

    Task<bool> IsPlaudRecordingDeletedAsync(string plaudRecordingId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
