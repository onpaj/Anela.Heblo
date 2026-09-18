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
    /// <summary>Parses "yyyy-MM" bounds; rejects from &gt; to, any month after <paramref name="currentMonth"/>, and ranges longer than <paramref name="maxMonths"/>.</summary>
    public static MonthRangeParseResult Parse(string? from, string? to, YearMonth currentMonth, int maxMonths)
    {
        if (!YearMonth.TryParse(from, out var f) || !YearMonth.TryParse(to, out var t) || f > t || t > currentMonth)
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
