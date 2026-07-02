using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Json;

public static class JsonResponseParser
{
    private static readonly Regex LeadingFence = new(
        @"^\s*```(?:json)?[ \t]*\r?\n?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingFence = new(
        @"\r?\n```\s*$",
        RegexOptions.Compiled);

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
            logger.LogWarning(ex, "{HandlerName}: failed to parse JSON response, using fallback. Cleaned input: {CleanedHead}",
                handlerName, cleaned[..Math.Min(cleaned.Length, 500)]);
            return fallback;
        }
        catch (InvalidOperationException ex)
        {
            // System.Text.Json throws InvalidOperationException (not JsonException) when a token
            // type mismatch occurs during deserialization (e.g. LLM returns "123" where int expected).
            logger.LogWarning(ex, "{HandlerName}: failed to parse JSON response (token type mismatch), using fallback. Cleaned input: {CleanedHead}",
                handlerName, cleaned[..Math.Min(cleaned.Length, 500)]);
            return fallback;
        }
    }

    public static bool TryParse<T>(
        string raw,
        [NotNullWhen(true)] out T? result,
        ILogger logger,
        [CallerMemberName] string handlerName = "")
    {
        result = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("{HandlerName}: received null or empty JSON response", handlerName);
            return false;
        }

        var cleaned = StripJsonFences(raw);

        try
        {
            result = JsonSerializer.Deserialize<T>(cleaned);
            return result is not null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "{HandlerName}: failed to parse JSON. Cleaned head: {CleanedHead}",
                handlerName, cleaned[..Math.Min(cleaned.Length, 500)]);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // System.Text.Json throws InvalidOperationException (not JsonException) when a token
            // type mismatch occurs during deserialization (e.g. LLM returns "123" where int expected).
            logger.LogWarning(ex, "{HandlerName}: failed to parse JSON (token type mismatch). Cleaned head: {CleanedHead}",
                handlerName, cleaned[..Math.Min(cleaned.Length, 500)]);
            return false;
        }
    }

    private static string StripJsonFences(string raw)
    {
        var trimmed = raw.Trim();
        trimmed = LeadingFence.Replace(trimmed, string.Empty, 1);
        trimmed = TrailingFence.Replace(trimmed, string.Empty, 1);
        return trimmed.Trim();
    }
}
