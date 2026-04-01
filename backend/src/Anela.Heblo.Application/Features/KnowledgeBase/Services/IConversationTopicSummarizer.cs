namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IConversationTopicSummarizer
{
    Task<IReadOnlyList<string>> SummarizeTopicsAsync(string fullText, CancellationToken ct = default);
}
