using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

/// <summary>
/// M.E.AI pipeline middleware that enriches KB answers by replacing (CODE) product annotations
/// with inline references: [Name](url) when a URL is present, or Name (CODE) otherwise.
/// Also strips trailing product name fragments the LLM sometimes appends after (CODE).
/// Product data is resolved from <see cref="IProductEnrichmentCache"/>.
/// </summary>
public class PostAnswerEnrichmentMiddleware : DelegatingChatClient
{
    // Captures (CODE) and optionally the word sequence immediately after it (stops at punctuation/newline).
    private static readonly Regex ProductCodePattern = new(
        @"\(([A-Z0-9]+)\)(?:\s+([\p{L}\d][\p{L}\d ]*))?",
        RegexOptions.Compiled);

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
        var enriched = ProductCodePattern.Replace(rawText, match =>
        {
            var code = match.Groups[1].Value;
            if (!lookup.TryGetValue(code, out var entry))
                return match.Value;

            var replacement = string.IsNullOrEmpty(entry.Url)
                ? $"{entry.ProductName} ({code})"
                : $"[{entry.ProductName}]({entry.Url})";

            // If the LLM appended the product name after the code (e.g. "(AKL001) Product name"),
            // drop that trailing fragment to avoid duplication.
            if (match.Groups[2].Success)
            {
                var trailing = match.Groups[2].Value.TrimEnd();
                if (IsProductNamePrefix(trailing, entry.ProductName))
                    return replacement;

                return $"{replacement} {trailing}";
            }

            return replacement;
        });

        if (enriched == rawText)
            return response;

        // Returns true when trailingText is a word-for-word prefix of productName (case-insensitive).
        // E.g. "Očistím tvář" is a prefix of "Očistím tvář 30ml".
        static bool IsProductNamePrefix(string trailingText, string productName)
        {
            var trailing = trailingText.Trim().ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name = productName.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (trailing.Length == 0 || trailing.Length > name.Length)
                return false;

            for (var i = 0; i < trailing.Length; i++)
            {
                if (trailing[i] != name[i])
                    return false;
            }

            return true;
        }

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
