using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Json;

public static class JsonResponseParser
{
    private static readonly Regex FencePattern = new(
        @"```(?:json)?\s*\r?\n(.*?)\r?\n?```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static T ParseOrFallback<T>(
        string raw,
        T fallback,
        ILogger logger,
        [CallerMemberName] string handlerName = "")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("{HandlerName}: received null or empty JSON response, using fallback", handlerName);
            return fallback;
        }

        var cleaned = StripJsonFences(raw);

        try
        {
            var result = JsonSerializer.Deserialize<T>(cleaned);
            return result ?? fallback;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "{HandlerName}: failed to parse JSON response, using fallback. Cleaned input: {Cleaned}", handlerName, cleaned[..Math.Min(cleaned.Length, 500)]);
            return fallback;
        }
    }

    private static string StripJsonFences(string raw)
    {
        var trimmed = raw.Trim();
        var match = FencePattern.Match(trimmed);
        return match.Success ? match.Groups[1].Value.Trim() : trimmed;
    }
}
