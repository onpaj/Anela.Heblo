using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class KnowledgeBaseDocIndexingStrategy : IIndexingStrategy
{
    private readonly DocumentChunker _chunker;
    private readonly IChunkSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public KnowledgeBaseDocIndexingStrategy(
        DocumentChunker chunker,
        IChunkSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _chunker = chunker;
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.KnowledgeBase;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var chunkTexts = _chunker.Chunk(cleanText);
        var chunks = new List<KnowledgeBaseChunk>();

        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var summary = await _summarizer.SummarizeAsync(chunkTexts[i], ct);
            var embeddings = await _embeddingGenerator.GenerateAsync([summary], cancellationToken: ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Summary = summary,
                DocumentType = DocumentType.KnowledgeBase,
                Embedding = embeddings[0].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
