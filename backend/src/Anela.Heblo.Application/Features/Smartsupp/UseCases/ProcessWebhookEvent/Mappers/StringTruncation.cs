using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;

internal static class StringTruncation
{
    public static string? Truncate(
        string? value,
        int maxLength,
        string fieldName,
        string? contextId,
        ILogger logger,
        ISmartsuppWebhookMetrics metrics)
    {
        if (value is null) return null;
        if (value.Length <= maxLength) return value;

        var cut = maxLength;
        // Avoid splitting a UTF-16 surrogate pair: if the last kept char is a high
        // surrogate, step back one so we never emit a lone high surrogate.
        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
            cut--;

        var truncated = value[..cut];

        logger.LogWarning(
            "smartsupp webhook field {Field} truncated original={OriginalLength} truncated={TruncatedLength} contextId={ContextId}",
            fieldName, value.Length, truncated.Length, contextId);

        metrics.RecordTruncation(fieldName);

        return truncated;
    }
}
