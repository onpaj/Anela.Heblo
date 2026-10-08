using System.Text.RegularExpressions;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// Google Ads ids are numeric. Every id that is interpolated into a URL or a GAQL query goes
/// through here first, so agent-supplied text can never reach either.
/// </summary>
internal static class GoogleAdsIds
{
    private static readonly Regex NumericId = new("^[0-9]{1,20}$", RegexOptions.CultureInvariant);

    public static string NormalizeCustomerId(string? value) =>
        (value ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    public static bool IsNumericId(string? value) => value is not null && NumericId.IsMatch(value);

    public static string RequireNumericId(string? value, string paramName) =>
        IsNumericId(value)
            ? value!
            : throw new ArgumentException($"'{value}' is not a numeric Google Ads id.", paramName);

    /// <summary>Splits "221~441" into its two numeric halves; false for anything else.</summary>
    public static bool TryParseComposite(string? value, out string first, out string second)
    {
        first = string.Empty;
        second = string.Empty;
        var parts = (value ?? string.Empty).Split('~');
        if (parts.Length != 2 || !IsNumericId(parts[0]) || !IsNumericId(parts[1]))
            return false;

        first = parts[0];
        second = parts[1];
        return true;
    }
}
