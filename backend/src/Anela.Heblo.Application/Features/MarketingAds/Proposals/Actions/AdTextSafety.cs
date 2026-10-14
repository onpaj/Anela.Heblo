using System.Globalization;
using System.Text;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>Detects characters that could break a rendered line or visually forge one (control, format, line/paragraph separators).</summary>
internal static class AdTextSafety
{
    /// <summary>
    /// Rune-based so supplementary-plane format characters (e.g. U+E0041 tag characters) are caught.
    /// Lone surrogates enumerate as U+FFFD and are treated as forbidden.
    /// </summary>
    public static bool IsForbidden(Rune rune) =>
        rune == Rune.ReplacementChar
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    public static bool ContainsForbidden(string value) => value.EnumerateRunes().Any(IsForbidden);
}
