namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>Czech public holidays (zákon č. 245/2000 Sb.). Fixed dates plus Good Friday
/// and Easter Monday derived from the Gregorian Easter (Meeus/Jones/Butcher algorithm).</summary>
public static class CzechHolidays
{
    private static readonly (int Month, int Day)[] FixedHolidays =
    {
        (1, 1), (5, 1), (5, 8), (7, 5), (7, 6), (9, 28), (10, 28), (11, 17), (12, 24), (12, 25), (12, 26)
    };

    public static bool IsPublicHoliday(DateOnly date)
    {
        if (FixedHolidays.Any(h => h.Month == date.Month && h.Day == date.Day))
        {
            return true;
        }

        var easterSunday = EasterSunday(date.Year);
        return date == easterSunday.AddDays(-2)   // Velký pátek
            || date == easterSunday.AddDays(1);   // Velikonoční pondělí
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
