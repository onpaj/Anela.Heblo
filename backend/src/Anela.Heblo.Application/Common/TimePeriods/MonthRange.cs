namespace Anela.Heblo.Application.Common.TimePeriods;

/// <summary>
/// Whole-calendar-month window helpers shared by the ledger-backed cost
/// aggregations. The Catalog cost providers still carry their own private
/// copies of this logic (GetDateRange / GenerateMonthRange); they adopt this
/// helper when their ledger pulls are deduplicated against CostPoolService.
/// </summary>
public static class MonthRange
{
    /// <summary>
    /// Expands a date range so it starts on the first day of <paramref name="from"/>'s
    /// month and ends on the last second of <paramref name="to"/>'s month.
    /// </summary>
    public static (DateTime Start, DateTime End) ToWholeMonths(DateOnly from, DateOnly to)
    {
        var start = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(
            to.Year, to.Month, DateTime.DaysInMonth(to.Year, to.Month), 23, 59, 59);

        return (start, end);
    }

    /// <summary>
    /// Yields the first day of every month touched by the range, inclusive.
    /// </summary>
    public static IEnumerable<DateTime> EnumerateMonths(DateTime start, DateTime end)
    {
        var current = new DateTime(start.Year, start.Month, 1);
        var last = new DateTime(end.Year, end.Month, 1);

        while (current <= last)
        {
            yield return current;
            current = current.AddMonths(1);
        }
    }
}
