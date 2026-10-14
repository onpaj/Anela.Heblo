using System.Globalization;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Sync;

/// <summary>
/// The Data API returns every metric as a string. Metrics can come back as "12", "12.0" or ""
/// depending on their type, so integer metrics are parsed as decimal and then truncated rather
/// than fed straight to long.Parse, which throws on "12.0".
/// </summary>
internal static class Ga4ValueParser
{
    public static long ToLong(string? value) => (long)ToDecimal(value);

    public static decimal ToDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    public static DateOnly ToDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new FormatException($"GA4 returned an unparseable 'date' dimension value: '{value}'.");

    /// <summary>
    /// GA4 uses "(not set)" / "(other)" for missing or bucketed dimension values. They are kept
    /// verbatim — hiding them would quietly change the totals — but never left as null, because
    /// the dimension is part of the primary key.
    /// </summary>
    public static string ToDimension(string? value) =>
        string.IsNullOrEmpty(value) ? "(not set)" : value;
}
