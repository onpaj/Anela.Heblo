namespace Anela.Heblo.Application.Features.Smartsupp;

public static class SmartsuppNameHelper
{
    private static readonly HashSet<string> FallbackNames =
        new(StringComparer.OrdinalIgnoreCase) { "Unknown User", "Anonymous" };

    public static string ExtractFirstName(string? fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || FallbackNames.Contains(trimmed))
            return "Anela";

        var firstName = trimmed.Split(' ')[0];
        return firstName.Length == 0 ? "Anela" : firstName;
    }
}
