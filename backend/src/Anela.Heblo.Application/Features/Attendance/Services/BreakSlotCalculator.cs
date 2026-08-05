namespace Anela.Heblo.Application.Features.Attendance.Services;

public static class BreakSlotCalculator
{
    private static readonly TimeSpan Rounding = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EdgeMargin = TimeSpan.FromMinutes(5);

    /// <summary>Merges overlapping/adjacent work intervals into continuous segments, sorted by start.</summary>
    public static IReadOnlyList<TimeSlot> BuildSegments(IEnumerable<TimeSlot> intervals)
    {
        var sorted = intervals.OrderBy(i => i.Start).ToList();
        var result = new List<TimeSlot>();

        foreach (var interval in sorted)
        {
            if (result.Count > 0 && interval.Start <= result[^1].End)
            {
                if (interval.End > result[^1].End)
                {
                    result[^1] = new TimeSlot(result[^1].Start, interval.End);
                }
            }
            else
            {
                result.Add(interval);
            }
        }

        return result;
    }

    /// <summary>
    /// Picks where the break goes. Preferred window wins when it lies strictly inside a
    /// segment (a break touching the segment edge would not interrupt the work).
    /// Otherwise the break is centered in the longest segment, rounded to 5 minutes.
    /// Returns null when no segment can contain the break away from its edges.
    /// </summary>
    public static TimeSlot? ComputeBreakSlot(
        IReadOnlyList<TimeSlot> workSegments,
        TimeSlot preferredWindow,
        TimeSpan breakDuration)
    {
        if (workSegments.Count == 0)
        {
            return null;
        }

        foreach (var segment in workSegments)
        {
            if (preferredWindow.Start > segment.Start && preferredWindow.End < segment.End)
            {
                return preferredWindow;
            }
        }

        var longest = workSegments.MaxBy(s => s.Duration)!;
        if (longest.Duration < breakDuration + EdgeMargin + EdgeMargin)
        {
            return null;
        }

        var center = longest.Start + (longest.Duration - breakDuration) / 2;
        var rounded = RoundToNearest(center, Rounding);
        var earliest = longest.Start + EdgeMargin;
        var latest = longest.End - breakDuration - EdgeMargin;
        var start = rounded < earliest ? earliest : (rounded > latest ? latest : rounded);

        return new TimeSlot(start, start + breakDuration);
    }

    private static DateTime RoundToNearest(DateTime value, TimeSpan interval)
    {
        var ticks = (long)Math.Round(value.Ticks / (double)interval.Ticks) * interval.Ticks;
        return new DateTime(ticks, value.Kind);
    }
}
