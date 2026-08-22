using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationIndexingStrategy : IIndexingStrategy
{
    private readonly IConversationTopicSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly KnowledgeBaseOptions _options;

    public ConversationIndexingStrategy(
        IConversationTopicSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<KnowledgeBaseOptions> options)
    {
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
        _options = options.Value;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.Conversation;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var topics = await _summarizer.SummarizeTopicsAsync(cleanText, ct);
        if (topics.Count == 0)
            return [];

        var embeddings = await _embeddingGenerator.GenerateAsync(topics, _options.ToEmbeddingOptions(), ct);
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
                DocumentType = DocumentType.Conversation,
                Embedding = embeddings[i].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
