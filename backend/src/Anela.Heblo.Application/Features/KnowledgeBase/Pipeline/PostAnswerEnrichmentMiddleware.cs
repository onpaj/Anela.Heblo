using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

/// <summary>
/// Pass-through M.E.AI middleware. Extension point for future product code and eshop URL enrichment.
/// </summary>
public class PostAnswerEnrichmentMiddleware : DelegatingChatClient
{
    public PostAnswerEnrichmentMiddleware(IChatClient inner)
        : base(inner)
    {
    }

    // Pass-through — enrichment logic to be added in a future issue
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(chatMessages, options, cancellationToken);
}
