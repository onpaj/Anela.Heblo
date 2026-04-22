using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

/// <summary>
/// M.E.AI pipeline middleware that enriches KB answers by replacing (CODE) product annotations
/// with inline references: [Name](url) when a URL is present, or Name (CODE) otherwise.
/// Product data is resolved from <see cref="IProductEnrichmentCache"/>.
/// </summary>
public class PostAnswerEnrichmentMiddleware : DelegatingChatClient
{
    private static readonly Regex ProductCodePattern = new(@"\(([A-Z0-9]+)\)", RegexOptions.Compiled);
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

            return string.IsNullOrEmpty(entry.Url)
                ? $"{entry.ProductName} ({code})"
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
