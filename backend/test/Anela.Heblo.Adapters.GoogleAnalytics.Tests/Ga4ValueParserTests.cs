using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// Every metric the Data API returns arrives as a string, so this parser stands between GA4's
/// wire format and every stored number. Its three decisions each have a failure mode that writes
/// wrong data rather than erroring.
/// </summary>
public class Ga4ValueParserTests
{
    [Theory]
    [InlineData("12", 12L)]
    [InlineData("12.0", 12L)]      // the whole reason this does not go through long.Parse
    [InlineData("12.7", 12L)]      // truncated, matching GA4's own integer semantics
    [InlineData("0", 0L)]
    [InlineData("", 0L)]           // GA4 returns "" for a metric with no value
    [InlineData(null, 0L)]
    public void reads_an_integer_metric_however_ga4_formatted_it(string? value, long expected)
    {
        Ga4ValueParser.ToLong(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("0.0", 0)]
    [InlineData("", 0)]
    public void reads_a_decimal_metric_with_an_invariant_decimal_point(string value, decimal expected)
    {
        // Invariant culture, not the host's: on a Czech server "1234.56" under cs-CZ would parse
        // as 123456, inflating revenue by a factor of 100.
        Ga4ValueParser.ToDecimal(value).Should().Be(expected);
    }

    [Fact]
    public void reads_a_date_dimension_in_ga4s_compact_format()
    {
        Ga4ValueParser.ToDate("20260308").Should().Be(new DateOnly(2026, 3, 8));
    }

    [Theory]
    [InlineData("2026-03-08")]
    [InlineData("")]
    [InlineData("(other)")]
    public void throws_on_an_unparseable_date_rather_than_storing_a_wrong_one(string value)
    {
        // A wrong date is worse than a failed chunk: it lands on the primary key, so the row is
        // silently attributed to another day and no later run corrects it.
        var act = () => Ga4ValueParser.ToDate(value);
        act.Should().Throw<FormatException>().WithMessage("*date*");
    }

    [Theory]
    [InlineData("", "(not set)")]
    [InlineData(null, "(not set)")]
    [InlineData("(other)", "(other)")]
    [InlineData("Organic Search", "Organic Search")]
    public void never_leaves_a_dimension_null_because_it_is_part_of_the_primary_key(string? value, string expected)
    {
        Ga4ValueParser.ToDimension(value).Should().Be(expected);
    }
}
