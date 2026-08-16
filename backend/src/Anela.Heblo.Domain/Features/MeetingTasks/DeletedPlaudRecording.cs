namespace Anela.Heblo.Domain.Features.MeetingTasks;

/// <summary>
/// Marks a Plaud recording whose meeting transcript was deleted by a user.
/// Prevents <c>PlaudPollingJob</c> from re-ingesting the recording while it is
/// still inside the polling window. Deliberately stores no meeting content —
/// only who deleted which recording and when.
/// </summary>
public class DeletedPlaudRecording
{
    public Guid Id { get; set; }

    public string PlaudRecordingId { get; set; } = null!;

    public DateTime DeletedAt { get; set; }

    public string DeletedByUserEmail { get; set; } = null!;
}
