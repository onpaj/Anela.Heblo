using System.Globalization;
using Anela.Heblo.API.Infrastructure;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure;

public class UtcDateTimeModelBinderTests
{
    // ------------------------------------------------------------------
    // Direct parsing-logic tests — inherently timezone-independent, since
    // AdjustToUniversal | AssumeUniversal ignores the local offset entirely.
    // The full ASP.NET Core binding pipeline (value provider wiring, the
    // automatic-400-on-failure behavior) is covered end-to-end by
    // BankStatementImportIntegrationTests.GetBankStatements_WithUtcDesignatorDateQueryParam_BindsSuccessfully
    // and GetBankStatements_WithInvalidDateFromQueryParam_ReturnsBadRequest.
    // ------------------------------------------------------------------

    [Fact]
    public void TryParseUtc_WithUtcDesignatorDateOnlyMidnight_ReturnsSameCalendarDate()
    {
        // This is the exact input the frontend sends for a date-only query param
        // (new Date('2026-01-01').toISOString() === "2026-01-01T00:00:00.000Z").
        // Regardless of server timezone, .Date must remain 2026-01-01.
        var success = UtcDateTimeModelBinder.TryParseUtc("2026-01-01T00:00:00.000Z", out var parsed);

        Assert.True(success);
        Assert.Equal(new DateTime(2026, 1, 1), parsed.Date);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00.000Z", "2026-01-01")]
    [InlineData("2026-06-15T23:59:59.000Z", "2026-06-15")]
    [InlineData("2026-01-01", "2026-01-01")]
    public void TryParseUtc_WithVariousValidInputs_PreservesExpectedCalendarDate(string input, string expectedDate)
    {
        var success = UtcDateTimeModelBinder.TryParseUtc(input, out var parsed);

        Assert.True(success);
        Assert.Equal(DateTime.Parse(expectedDate, CultureInfo.InvariantCulture).Date, parsed.Date);
    }

    [Fact]
    public void TryParseUtc_WithInvalidValue_ReturnsFalse()
    {
        var success = UtcDateTimeModelBinder.TryParseUtc("not-a-date", out _);

        Assert.False(success);
    }
}
