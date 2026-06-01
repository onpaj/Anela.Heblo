using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.Photobank;

public static class TagRulePatternTranslator
{
    public static string Translate(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (pattern.StartsWith('^'))
            return pattern;

        var segments = pattern.Split('/');
        var regexSegments = new string[segments.Length];

        for (var i = 0; i < segments.Length; i++)
        {
            regexSegments[i] = segments[i] == "*"
                ? "[^/]+"
                : Regex.Escape(segments[i]);
        }

        return "^" + string.Join("/", regexSegments) + "(/|$)";
    }
}
