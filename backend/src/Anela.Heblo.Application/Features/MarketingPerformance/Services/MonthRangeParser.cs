using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class MonthRangeParseResult
{
    public YearMonth From { get; init; }
    public YearMonth To { get; init; }
    public ErrorCodes? Error { get; init; }
    public Dictionary<string, string>? Params { get; init; }
    public bool IsValid => Error is null;
    public int MonthCount => YearMonth.MonthsBetween(From, To);
}

public static class MonthRangeParser
{
    /// <summary>Earliest month this feature has source data for; without a floor "0001-01" parses and is accepted.</summary>
    public static readonly YearMonth EarliestSupportedMonth = new(2020, 1);

    /// <summary>Parses "yyyy-MM" bounds; rejects from &gt; to, anything before <see cref="EarliestSupportedMonth"/>, any month after <paramref name="currentMonth"/>, and ranges longer than <paramref name="maxMonths"/>.</summary>
    public static MonthRangeParseResult Parse(string? from, string? to, YearMonth currentMonth, int maxMonths)
    {
        if (!YearMonth.TryParse(from, out var f) || !YearMonth.TryParse(to, out var t) || f > t || t > currentMonth || f < EarliestSupportedMonth)
        {
            return new MonthRangeParseResult { Error = ErrorCodes.MarketingPerformanceInvalidMonthRange, Params = new() { { "from", from ?? "" }, { "to", to ?? "" } } };
        }
        if (YearMonth.MonthsBetween(f, t) > maxMonths)
        {
            return new MonthRangeParseResult { Error = ErrorCodes.MarketingPerformanceRangeTooLarge, Params = new() { { "maxMonths", maxMonths.ToString() } } };
        }
        return new MonthRangeParseResult { From = f, To = t };
    }
}
