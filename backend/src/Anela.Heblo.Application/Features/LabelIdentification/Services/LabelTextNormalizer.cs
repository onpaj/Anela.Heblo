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
    // Verified: all 37 reference PDFs contain exactly one "Ingredients" marker,
    // always on the first page — the lazy match here only ever strips page 1's
    // header and correctly leaves later pages' ingredient-list continuation alone.
    private static readonly Regex IngredientsPrefix =
        new(@"^.*?ingredients\s*:", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The same job-name stamp also recurs at the end of EVERY extracted page (not
    // just as a document-level prefix) — e.g.
    // "Anela_Malá čarodějka_15ml_kelimek-dno_32mm_bila-snimatelna". Verified against
    // PdfPig's own page-by-page text for all 37 reference PDFs (50 stamp occurrences
    // across single- and multi-page PDFs): every occurrence matches this pattern
    // with no trailing content, i.e. the stamp always runs to the exact end of its
    // page's text. Left in, it injects the sticker SIZE (15ml vs 30ml) into the
    // normalized text, which breaks the property that two size variants of the same
    // family must normalize identically — and it injects tokens (the Czech product
    // nickname, "kelimek-dno", "sandwich"/"snimatelna") that OCR of the physical
    // sticker can never produce, depressing every match score.
    private static readonly Regex JobNameStamp = new(
        @"anela_.*?\d+ml_kelimek-dno_\d+mm[-_](?:sandwich|bila-snimatelna)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        // Strip the stamp BEFORE lowercasing/charset-filtering, while "_" and
        // diacritics are still intact and the pattern is still recognizable.
        text = JobNameStamp.Replace(text, string.Empty);

        text = text.ToLowerInvariant();
        text = NonCanonical.Replace(text, " ");
        text = Whitespace.Replace(text, " ");
        text = SpaceBeforeComma.Replace(text, ",");

        return text.Trim();
    }
}
