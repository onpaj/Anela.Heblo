using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class KnowledgeBaseDocIndexingStrategy : IIndexingStrategy
{
    private readonly IWordWindowChunker _chunker;
    private readonly IChunkSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly KnowledgeBaseOptions _options;

    public KnowledgeBaseDocIndexingStrategy(
        IWordWindowChunker chunker,
        IChunkSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<KnowledgeBaseOptions> options)
    {
        _chunker = chunker;
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
        _options = options.Value;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.KnowledgeBase;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var chunkTexts = _chunker.Chunk(cleanText, _options.ChunkSize, _options.ChunkOverlap);
        if (chunkTexts.Count == 0)
            return [];

        var summaries = new List<string>(chunkTexts.Count);
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            summaries.Add(await _summarizer.SummarizeAsync(chunkTexts[i], ct));
        }

        var embeddings = await _embeddingGenerator.GenerateAsync(summaries, cancellationToken: ct);
        var chunks = new List<KnowledgeBaseChunk>(chunkTexts.Count);

        for (var i = 0; i < chunkTexts.Count; i++)
        {
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Summary = summaries[i],
                DocumentType = DocumentType.KnowledgeBase,
                Embedding = embeddings[i].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
