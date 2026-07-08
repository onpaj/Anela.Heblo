using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

/// <summary>
/// Normalizes retrieved context before it is injected into the answer prompt.
/// Historical conversations often contain human-authored Markdown product links
/// ([Name](url)). Left in the context, the model imitates that pattern and copies
/// stale URLs instead of emitting the instructed [Name](CODE) format. Stripping the
/// links down to their plain product name removes the competing example so product
/// formatting is resolved consistently by <see cref="PostAnswerEnrichmentMiddleware"/>.
/// </summary>
public static class ContextSanitizer
{
    private static readonly Regex MarkdownLinkPattern =
        new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);

    /// <summary>
    /// Replaces every Markdown link with its link text, leaving all other content intact.
    /// </summary>
    public static string StripMarkdownLinks(string context) =>
        string.IsNullOrEmpty(context)
            ? context
            : MarkdownLinkPattern.Replace(context, "$1");
}
