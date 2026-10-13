namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Returns error params, or null when valid. Bounded ranges keep agent queries cheap on the B1ms server.</summary>
public static class AdReadValidation
{
    public static Dictionary<string, string>? DateRange(DateOnly from, DateOnly to, int maxDays) =>
        from > to || to.DayNumber - from.DayNumber + 1 > maxDays
            ? new Dictionary<string, string> { ["From"] = from.ToString("yyyy-MM-dd"), ["To"] = to.ToString("yyyy-MM-dd"), ["MaxDays"] = maxDays.ToString() }
            : null;

    public static Dictionary<string, string>? Positive(string field, int value, int max) =>
        value < 1 || value > max
            ? new Dictionary<string, string> { ["Field"] = field, ["Value"] = value.ToString(), ["Max"] = max.ToString() }
            : null;

    /// <summary>Bounds PageNumber so the repository's (PageNumber - 1) * PageSize cannot overflow int.</summary>
    public const int MaxPageNumber = 10_000;

    public static Dictionary<string, string>? UndefinedEnum<TEnum>(string field, TEnum value) where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? null
            : new Dictionary<string, string> { ["Field"] = field, ["Value"] = value.ToString() };

    /// <summary>Optional filter: null is valid, a set value must be defined.</summary>
    public static Dictionary<string, string>? UndefinedEnum<TEnum>(string field, TEnum? value) where TEnum : struct, Enum =>
        value is { } v ? UndefinedEnum(field, v) : null;

    public static Dictionary<string, string> NotConfigured(string reason = "AdsDatabaseNotConfigured") => new() { ["Reason"] = reason };
}
