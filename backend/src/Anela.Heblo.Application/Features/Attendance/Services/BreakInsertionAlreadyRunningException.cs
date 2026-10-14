namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>
/// Thrown when a break-insertion walk is requested while another one is still in flight. The walk
/// reads a window and then writes to it, so two overlapping runs would both decide a day has no
/// break and insert one each.
/// </summary>
public class BreakInsertionAlreadyRunningException : InvalidOperationException
{
    public BreakInsertionAlreadyRunningException()
        : base("A break insertion run is already in progress.")
    {
    }
}
