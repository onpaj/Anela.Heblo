using System.Text.Json;

namespace Anela.Heblo.Application.Features.MarketingAds.Sync;

/// <summary>old_value / new_value are jsonb: a platform value that is not valid JSON would fail the insert.</summary>
public static class AdJsonValues
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(value);
        }
    }
}
