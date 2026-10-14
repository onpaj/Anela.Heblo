using System.Globalization;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>Detects characters that could break a rendered line or visually forge one (control, format, line/paragraph separators).</summary>
internal static class AdTextSafety
{
    public static bool IsForbidden(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    public static bool ContainsForbidden(string value) => value.Any(IsForbidden);
}
