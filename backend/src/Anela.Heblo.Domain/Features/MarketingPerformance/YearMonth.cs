using System.Globalization;

namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>Calendar month key used by the marketing performance snapshot.</summary>
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public DateTime Start => new(Year, Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
    public DateTime EndExclusive => Start.AddMonths(1);
    public DateTime LastDay => EndExclusive.AddDays(-1);

    public YearMonth AddMonths(int months)
    {
        var d = Start.AddMonths(months);
        return new YearMonth(d.Year, d.Month);
    }

    public static YearMonth From(DateTime date) => new(date.Year, date.Month);

    public static YearMonth Parse(string value)
    {
        if (!TryParse(value, out var result))
            throw new FormatException($"'{value}' is not a valid yyyy-MM month.");
        return result;
    }

    public static bool TryParse(string? value, out YearMonth result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        result = new YearMonth(d.Year, d.Month);
        return true;
    }

    public static IEnumerable<YearMonth> Range(YearMonth from, YearMonth toInclusive)
    {
        for (var ym = from; ym <= toInclusive; ym = ym.AddMonths(1))
            yield return ym;
    }

    public static int MonthsBetween(YearMonth from, YearMonth toInclusive) =>
        (toInclusive.Year - from.Year) * 12 + (toInclusive.Month - from.Month) + 1;

    public int CompareTo(YearMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);
    public static bool operator <(YearMonth a, YearMonth b) => a.CompareTo(b) < 0;
    public static bool operator >(YearMonth a, YearMonth b) => a.CompareTo(b) > 0;
    public static bool operator <=(YearMonth a, YearMonth b) => a.CompareTo(b) <= 0;
    public static bool operator >=(YearMonth a, YearMonth b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
