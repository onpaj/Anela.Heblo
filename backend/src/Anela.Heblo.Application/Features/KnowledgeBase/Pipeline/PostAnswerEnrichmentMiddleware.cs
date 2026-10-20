using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

/// <summary>
/// M.E.AI pipeline middleware that turns product references in KB answers into presentation.
/// The model is instructed to emit products as [Name](CODE) Markdown links (name = naturally
/// inflected display text, target = product code). This middleware resolves the code to a URL:
/// [Name](url) when a URL is present, or the plain Name otherwise — the customer never sees a
/// raw product code. A legacy fallback also resolves bare (CODE) tokens for resilience.
/// Product data is resolved from <see cref="IProductEnrichmentCache"/>.
/// </summary>
public class PostAnswerEnrichmentMiddleware : DelegatingChatClient
{
    /// <summary>
    /// <see cref="ChatResponse.AdditionalProperties"/> key under which the catalog codes this middleware
    /// resolved are exposed (string[], known codes only, de-duplicated, first-appearance order). The
    /// middleware is a singleton and cannot reach the scoped interaction recorder, so the caller reads this.
    /// </summary>
    public const string RecommendedProductCodesKey = "anela.recommendedProductCodes";

    // [Name](CODE) — the instructed format. The code group only matches an all-caps/digit token,
    // so real Markdown URLs (lowercase, ':', '/') are never mistaken for a product code.
    private static readonly Regex LinkedProductPattern =
        new(@"\[(?<name>[^\]]+)\]\((?<code>[A-Z0-9]+)\)", RegexOptions.Compiled);

    // Bare (CODE) — legacy/fallback for answers where the model omitted the name.
    private static readonly Regex BareCodePattern =
        new(@"\((?<code>[A-Z0-9]+)\)", RegexOptions.Compiled);

    private readonly IProductEnrichmentCache _cache;

    public PostAnswerEnrichmentMiddleware(IChatClient inner, IProductEnrichmentCache cache)
        : base(inner)
    {
        _cache = cache;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        var rawText = response.Text ?? string.Empty;

        if (string.IsNullOrEmpty(rawText))
            return response;

        var lookup = await _cache.GetProductLookupAsync(cancellationToken);

        // Primary: [Name](CODE) — keep the model's inflected name, resolve the code to a URL.
        var enriched = LinkedProductPattern.Replace(rawText, match =>
        {
            var name = match.Groups["name"].Value;
            var code = match.Groups["code"].Value;

            // Unknown/hallucinated code: keep the readable name, drop the code.
            if (!lookup.TryGetValue(code, out var entry))
                return name;

            return string.IsNullOrEmpty(entry.Url)
                ? name
                : $"[{name}]({entry.Url})";
        });

        // Fallback: bare (CODE) with no name — resolve using the catalog name so the
        // customer never sees a raw code, even when the model ignores the link format.
        enriched = BareCodePattern.Replace(enriched, match =>
        {
            var code = match.Groups["code"].Value;

            // Not a known product code: leave the token untouched (may be unrelated).
            if (!lookup.TryGetValue(code, out var entry))
                return match.Value;

            return string.IsNullOrEmpty(entry.Url)
                ? entry.ProductName
                : $"[{entry.ProductName}]({entry.Url})";
        });

        if (enriched == rawText)
            return response;

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, enriched)])
        {
            ResponseId = response.ResponseId,
            ConversationId = response.ConversationId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            AdditionalProperties = WithRecommendedCodes(response.AdditionalProperties, ResolveCodesInTextOrder(rawText, lookup)),
        };
    }

    // Known codes ordered by earliest position in the ORIGINAL text (the two rewrite passes run
    // sequentially, so their own order is not text order). A code inside a linked match is also a
    // bare match at a later index; the earliest position wins.
    private static IReadOnlyList<string> ResolveCodesInTextOrder(
        string rawText,
        IReadOnlyDictionary<string, ProductEnrichmentEntry> lookup) =>
        LinkedProductPattern.Matches(rawText)
            .Concat(BareCodePattern.Matches(rawText))
            .Where(m => lookup.ContainsKey(m.Groups["code"].Value))
            .OrderBy(m => m.Index)
            .Select(m => m.Groups["code"].Value)
            .ToList();

    // Copies rather than mutates: the inner response's dictionary may be shared with other consumers.
    private static AdditionalPropertiesDictionary WithRecommendedCodes(
        AdditionalPropertiesDictionary? source,
        IReadOnlyList<string> resolvedCodes)
    {
        var copy = new AdditionalPropertiesDictionary();
        if (source is not null)
        {
            foreach (var (key, value) in source)
                copy[key] = value;
        }

        copy[RecommendedProductCodesKey] = resolvedCodes.Distinct(StringComparer.Ordinal).ToArray();
        return copy;
    }
}
