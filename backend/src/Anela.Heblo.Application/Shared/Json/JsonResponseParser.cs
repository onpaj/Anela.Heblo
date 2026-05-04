using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Json;

public static class JsonResponseParser
{
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
            logger.LogWarning(ex, "{HandlerName}: failed to parse JSON response, using fallback", handlerName);
            return fallback;
        }
    }

    private static string StripJsonFences(string raw)
    {
        var trimmed = raw.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var withoutOpening = trimmed[(firstNewline + 1)..];

        if (withoutOpening.EndsWith("```", StringComparison.Ordinal))
        {
            withoutOpening = withoutOpening[..^3];
        }

        return withoutOpening.Trim();
    }
}
