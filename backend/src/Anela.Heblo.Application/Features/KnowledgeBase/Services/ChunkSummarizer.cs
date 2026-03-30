using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ChunkSummarizer : IChunkSummarizer
{
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;

    public ChunkSummarizer(IChatClient chatClient, IOptions<KnowledgeBaseOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<string> SummarizeAsync(string chunkText, CancellationToken ct = default)
    {
        if (!_options.SummarizationEnabled)
            return chunkText;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, _options.SummarizationPrompt + "\n" + chunkText)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? chunkText;
    }
}
