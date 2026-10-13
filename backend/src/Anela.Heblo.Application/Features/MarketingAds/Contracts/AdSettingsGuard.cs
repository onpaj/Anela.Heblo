namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Decides whether platform settings are really configured. appsettings.json ships placeholders
/// ("-- stored in secrets.json --", "act_XXXXXXXXX", "XXX-XXX-XXXX", "your-…"), and a Key Vault
/// placeholder survives a plain IsNullOrWhiteSpace check — registering a platform on such a value
/// would make every call fail at runtime (or, with eager parsing, stop the app booting).
/// Known limitation (spec 12.2): a genuine secret containing "XXX" is treated as unconfigured.
/// </summary>
public static class AdSettingsGuard
{
    private const string PlaceholderPrefix = "--";
    private const string TemplateMarker = "XXX";
    private const string TemplatePrefix = "your-";

    public static bool IsConfigured(params string?[] values) =>
        values is { Length: > 0 } && values.All(IsRealValue);

    private static bool IsRealValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return !trimmed.StartsWith(PlaceholderPrefix, StringComparison.Ordinal)
            && !trimmed.Contains(TemplateMarker, StringComparison.Ordinal)
            && !trimmed.StartsWith(TemplatePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
