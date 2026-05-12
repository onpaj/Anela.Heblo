namespace Anela.Heblo.Application.Common.TimePeriods;

public class TimePeriodResolver : ITimePeriodResolver
{
    public IReadOnlyList<DateRange> Resolve(TimePeriod period, DateTime? customFrom = null, DateTime? customTo = null)
    {
        var now = DateTime.UtcNow;

        return period switch
        {
            TimePeriod.PreviousQuarter => ResolveForPreviousQuarter(now),
            TimePeriod.FutureQuarter => ResolveForFutureQuarter(now),
            TimePeriod.Y2Y => ResolveForY2Y(now),
            TimePeriod.PreviousSeason => ResolveForPreviousSeason(now),
            TimePeriod.Q9M => ResolveForQ9M(now),
            TimePeriod.CustomPeriod when customFrom.HasValue && customTo.HasValue =>
                [new DateRange(customFrom.Value, customTo.Value)],
            TimePeriod.CustomPeriod => [],
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown time period"),
        };
    }

    private static IReadOnlyList<DateRange> ResolveForPreviousQuarter(DateTime now)
    {
        var startOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
        var endOfPreviousMonth = startOfCurrentMonth.AddDays(-1);
        var startOfPreviousQuarter = startOfCurrentMonth.AddMonths(-3);
        return [new DateRange(startOfPreviousQuarter, endOfPreviousMonth)];
    }

    private static IReadOnlyList<DateRange> ResolveForFutureQuarter(DateTime now)
    {
        var startOfFutureQuarterLastYear = new DateTime(now.Year - 1, now.Month, 1);
        var endOfFutureQuarterLastYear = startOfFutureQuarterLastYear.AddMonths(3).AddDays(-1);
        return [new DateRange(startOfFutureQuarterLastYear, endOfFutureQuarterLastYear)];
    }

    private static IReadOnlyList<DateRange> ResolveForY2Y(DateTime now)
    {
        var startOfY2Y = new DateTime(now.Year, now.Month, 1).AddMonths(-12);
        var endOfY2Y = new DateTime(now.Year, now.Month, 1).AddDays(-1);
        return [new DateRange(startOfY2Y, endOfY2Y)];
    }

    private static IReadOnlyList<DateRange> ResolveForPreviousSeason(DateTime now)
    {
        var seasonStart = new DateTime(now.Year - 1, 10, 1);
        var seasonEnd = new DateTime(now.Year, 1, 31);
        return [new DateRange(seasonStart, seasonEnd)];
    }

    private static IReadOnlyList<DateRange> ResolveForQ9M(DateTime now)
    {
        var rangeAFrom = now.AddMonths(-6);
        var rangeATo = now;
        var rangeBFrom = now.AddYears(-1);
        var rangeBTo = now.AddYears(-1).AddMonths(3);
        return [new DateRange(rangeAFrom, rangeATo), new DateRange(rangeBFrom, rangeBTo)];
    }
}
