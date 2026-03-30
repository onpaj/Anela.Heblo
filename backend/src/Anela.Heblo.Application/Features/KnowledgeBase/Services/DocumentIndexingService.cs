using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentIndexingService : IDocumentIndexingService
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly DocumentChunker _chunker;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ChatTranscriptPreprocessor _preprocessor;
    private readonly IChunkSummarizer _summarizer;

    public DocumentIndexingService(
        IEnumerable<IDocumentTextExtractor> extractors,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        DocumentChunker chunker,
        IKnowledgeBaseRepository repository,
        ChatTranscriptPreprocessor preprocessor,
        IChunkSummarizer summarizer)
    {
        _extractors = extractors;
        _embeddingGenerator = embeddingGenerator;
        _chunker = chunker;
        _repository = repository;
        _preprocessor = preprocessor;
        _summarizer = summarizer;
    }

    public async Task IndexChunksAsync(
        byte[] content,
        string contentType,
        KnowledgeBaseDocument document,
        CancellationToken ct = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(contentType))
            ?? throw new NotSupportedException($"Content type '{contentType}' is not supported.");

        var text = await extractor.ExtractTextAsync(content, ct);
        text = _preprocessor.Clean(text);
        var chunkTexts = _chunker.Chunk(text);

        var chunks = new List<KnowledgeBaseChunk>();
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var summary = await _summarizer.SummarizeAsync(chunkTexts[i], ct);
            var embeddings = await _embeddingGenerator.GenerateAsync(
                [summary],
                cancellationToken: ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Embedding = embeddings[0].Vector.ToArray(),
            });
        }

        await _repository.AddChunksAsync(chunks, ct);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;
    }
}
