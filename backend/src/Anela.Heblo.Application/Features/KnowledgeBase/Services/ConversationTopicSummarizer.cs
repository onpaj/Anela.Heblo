using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationTopicSummarizer : IConversationTopicSummarizer
{
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;

    public ConversationTopicSummarizer(IChatClient chatClient, IOptions<KnowledgeBaseOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<string>> SummarizeTopicsAsync(
        string fullText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullText))
            return [];

        if (!_options.SummarizationEnabled)
            return [fullText];

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, _options.TopicSummarizationPrompt + "\n" + fullText)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var responseText = response.Text ?? fullText;

        var topics = responseText
            .Split(_options.TopicDelimiter, StringSplitOptions.None)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return topics.Count > 0 ? topics : [fullText];
    }
}
