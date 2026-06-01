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

    public async Task<string> ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(config.Prompt))
        {
            return query;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, config.Prompt + "\n" + query)
        };

        var chatOptions = new ChatOptions { ModelId = config.Model };
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            return string.IsNullOrWhiteSpace(response.Text) ? query : response.Text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Query expansion failed for query '{Query}', falling back to raw query", query);
            return query;
        }
    }
}
