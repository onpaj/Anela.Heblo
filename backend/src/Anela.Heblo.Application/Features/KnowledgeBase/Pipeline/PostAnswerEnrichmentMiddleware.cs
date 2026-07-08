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
            AdditionalProperties = response.AdditionalProperties,
        };
    }
}
