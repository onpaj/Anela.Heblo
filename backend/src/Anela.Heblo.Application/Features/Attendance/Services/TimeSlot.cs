namespace Anela.Heblo.Application.Features.Attendance.Services;

/// <summary>Half-open local-time interval [Start, End). Times are Prague wall clock.</summary>
public sealed record TimeSlot(DateTime Start, DateTime End)
{
    public TimeSpan Duration => End - Start;
}
