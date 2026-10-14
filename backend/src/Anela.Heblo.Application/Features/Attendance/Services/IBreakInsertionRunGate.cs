namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>
/// Lets exactly one break-insertion walk run at a time. The walk reads a window and then writes to
/// it, so two overlapping runs — the nightly job and an on-demand sweep, typically — would each see
/// "no break on this day" and insert one each. Registered as a singleton; the app runs as a single
/// container, so a process-wide gate is enough.
/// </summary>
public interface IBreakInsertionRunGate
{
    /// <summary>Takes the gate. Returns false when a walk is already in flight.</summary>
    bool TryEnter();

    /// <summary>Releases the gate. Safe to call only after <see cref="TryEnter"/> returned true.</summary>
    void Exit();
}

public sealed class BreakInsertionRunGate : IBreakInsertionRunGate
{
    private int _running;

    public bool TryEnter() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    public void Exit() => Interlocked.Exchange(ref _running, 0);
}
