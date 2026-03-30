using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationIndexingStrategy : IIndexingStrategy
{
    private readonly IConversationTopicSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public ConversationIndexingStrategy(
        IConversationTopicSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.Conversation;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var topics = await _summarizer.SummarizeTopicsAsync(cleanText, ct);
        if (topics.Count == 0)
            return [];

        var embeddings = await _embeddingGenerator.GenerateAsync(topics, cancellationToken: ct);
        var chunks = new List<KnowledgeBaseChunk>(topics.Count);

        for (var i = 0; i < topics.Count; i++)
        {
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = cleanText,
                Summary = topics[i],
                Embedding = embeddings[i].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
