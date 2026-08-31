using System.Globalization;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;

/// <summary>
/// Month-string arithmetic for product statistics. Months travel as "yyyy-MM" strings
/// rather than DateTime: a month is not an instant, and treating it as one invites
/// timezone drift between backend, JSON and browser.
/// </summary>
public static class MonthRange
{
    public static string Key(int year, int month) => $"{year:D4}-{month:D2}";

    public static string Key(DateTime date) => Key(date.Year, date.Month);

    public static bool TryParse(string month, out int year, out int monthNumber)
    {
        year = 0;
        monthNumber = 0;

        if (string.IsNullOrWhiteSpace(month) || month.Length != 7 || month[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(month.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear))
        {
            return false;
        }

        if (!int.TryParse(month.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMonth))
        {
            return false;
        }

        if (parsedMonth < 1 || parsedMonth > 12)
        {
            return false;
        }

        year = parsedYear;
        monthNumber = parsedMonth;
        return true;
    }

    /// <summary>
    /// Ascending, inclusive list of month keys. The lower bound is clamped to
    /// <see cref="CatalogConstants.HISTORY_FLOOR_DATE"/>; an inverted or unparseable
    /// range yields an empty list.
    /// </summary>
    public static List<string> Expand(string dateFrom, string dateTo)
    {
        if (!TryParse(dateFrom, out var fromYear, out var fromMonth) ||
            !TryParse(dateTo, out var toYear, out var toMonth))
        {
            return new List<string>();
        }

        var from = new DateTime(fromYear, fromMonth, 1);
        var to = new DateTime(toYear, toMonth, 1);

        var floor = new DateTime(
            CatalogConstants.HISTORY_FLOOR_DATE.Year,
            CatalogConstants.HISTORY_FLOOR_DATE.Month,
            1);

        if (from < floor)
        {
            from = floor;
        }

        var months = new List<string>();
        for (var cursor = from; cursor <= to; cursor = cursor.AddMonths(1))
        {
            months.Add(Key(cursor.Year, cursor.Month));
        }

        return months;
    }
}
