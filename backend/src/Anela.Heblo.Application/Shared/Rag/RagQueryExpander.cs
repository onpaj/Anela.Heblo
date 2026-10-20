using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Shared.Rag;

public sealed class RagQueryExpander : IRagQueryExpander
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<RagQueryExpander> _logger;

    public RagQueryExpander(IChatClient chatClient, ILogger<RagQueryExpander> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<RagExpansionResult> ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(config.Prompt))
        {
            return RagExpansionResult.Untagged(query);
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, config.Prompt + "\n" + query)
        };

        var chatOptions = new ChatOptions { ModelId = config.Model };
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            if (string.IsNullOrWhiteSpace(response.Text))
                return RagExpansionResult.Untagged(query);

            var parsed = RagExpansionParser.Parse(response.Text);
            if (parsed.IsMalformedJson)
            {
                _logger.LogWarning(
                    "Query expansion returned malformed JSON for query '{Query}'; embedding raw response without tags",
                    query);
            }

            return parsed.Expansion;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Query expansion failed for query '{Query}', falling back to raw query", query);
            return RagExpansionResult.Untagged(query);
        }
    }
}
