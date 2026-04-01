namespace Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters;

internal static class ManufactureErrorParsingHelpers
{
    internal static string ExtractBetweenQuotes(string message, string marker)
    {
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return "neznámý";

        start += marker.Length;
        var end = message.IndexOf("'", start, StringComparison.Ordinal);
        return end > start ? message[start..end] : "neznámý";
    }

    internal static (string required, string available) ExtractQuantities(string message)
    {
        var parenStart = message.LastIndexOf('(');
        var parenEnd = message.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart)
            return ("?", "?");

        var inside = message[(parenStart + 1)..parenEnd];
        var required = ExtractAfter(inside, "požadováno: ", ", dostupné:");
        var available = ExtractAfter(inside, "dostupné: ", null);
        return (required.Trim(), available.Trim());
    }

    internal static string ExtractAfter(string text, string marker, string? terminator)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return "?";

        start += marker.Length;
        if (terminator == null)
            return text[start..].Trim();

        var end = text.IndexOf(terminator, start, StringComparison.Ordinal);
        return end > start ? text[start..end] : "?";
    }
}
