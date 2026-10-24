namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Tracks consecutive polling runs that failed because Plaud's upstream API was unavailable.
/// Singleton, in-memory; thread-safe.
/// </summary>
public sealed class PlaudPollingHealthTracker
{
    private int _consecutiveFailures;

    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>Records one failed run and returns the new consecutive-failure count.</summary>
    public int RecordUpstreamFailure() => Interlocked.Increment(ref _consecutiveFailures);

    public void Reset() => Interlocked.Exchange(ref _consecutiveFailures, 0);
}
