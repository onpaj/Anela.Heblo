using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public class LeafletChunkSummarizer : ILeafletChunkSummarizer
{
    private readonly IChatClient _chatClient;
    private readonly LeafletOptions _options;

    public LeafletChunkSummarizer(IChatClient chatClient, IOptions<LeafletOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default)
    {
        if (!_options.SummarizationEnabled || string.IsNullOrWhiteSpace(chunkText))
            return chunkText;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, _options.SummarizationPrompt + "\n" + chunkText)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? chunkText;
    }
}
