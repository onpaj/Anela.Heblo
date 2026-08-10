namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public static class WorkingDaysCalculator
{
    /// <summary>Weekdays (Mon–Fri) in [from, toInclusive] that are not Czech public holidays.</summary>
    public static int CountWorkingDays(DateOnly from, DateOnly toInclusive)
    {
        var count = 0;
        for (var day = from; day <= toInclusive; day = day.AddDays(1))
        {
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (!isWeekend && !CzechHolidays.IsPublicHoliday(day))
            {
                count++;
            }
        }

        return count;
    }
}
