namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Thrown by <see cref="IMeetingTaskExtractor.ExtractAsync"/> when the LLM's response
/// could not be parsed into a valid, schema-conforming payload after exhausting all
/// retry attempts. Callers must not treat this as "zero tasks found" — it signals the
/// extraction itself failed and no tasks could be recovered.
/// </summary>
public sealed class MeetingTaskExtractionFailedException : Exception
{
    /// <summary>Total number of attempts made (initial call + retries) before giving up.</summary>
    public int AttemptCount { get; }

    /// <summary>The raw (fence-stripped) response text from the final failed attempt, for diagnostics.</summary>
    public string? LastRawResponse { get; }

    public MeetingTaskExtractionFailedException(string message, int attemptCount, string? lastRawResponse)
        : base(message)
    {
        AttemptCount = attemptCount;
        LastRawResponse = lastRawResponse;
    }
}
