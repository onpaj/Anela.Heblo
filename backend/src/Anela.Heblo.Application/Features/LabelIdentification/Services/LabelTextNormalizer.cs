using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

/// <summary>
/// Reduces label text to a canonical form for matching. Called by both the offline
/// reference extractor and the runtime OCR path — one implementation so the two ends
/// cannot drift apart.
/// </summary>
public static class LabelTextNormalizer
{
    // The artwork PDFs carry a Czech job-name line above the sticker die-cut
    // (e.g. "Anela_Malá čarodějka_15ml_k") that is never printed on the physical
    // sticker. It also carries the size, which would break family identity.
    // Verified: all 37 reference PDFs contain exactly one "Ingredients" marker.
    private static readonly Regex IngredientsPrefix =
        new(@"^.*?ingredients\s*:", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HyphenLineBreak = new(@"-\s*\n", RegexOptions.Compiled);
    private static readonly Regex NonCanonical = new(@"[^a-z0-9, ]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforeComma = new(@"\s+,", RegexOptions.Compiled);

    public static string Normalize(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        // Join hyphenation BEFORE collapsing whitespace, or the newline is already gone.
        var text = HyphenLineBreak.Replace(rawText, string.Empty);
        text = IngredientsPrefix.Replace(text, string.Empty);
        text = text.ToLowerInvariant();
        text = NonCanonical.Replace(text, " ");
        text = Whitespace.Replace(text, " ");
        text = SpaceBeforeComma.Replace(text, ",");

        return text.Trim();
    }
}
