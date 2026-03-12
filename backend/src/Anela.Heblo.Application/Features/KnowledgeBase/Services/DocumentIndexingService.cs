using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentIndexingService : IDocumentIndexingService
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IEmbeddingService _embeddingService;
    private readonly DocumentChunker _chunker;
    private readonly IKnowledgeBaseRepository _repository;

    public DocumentIndexingService(
        IEnumerable<IDocumentTextExtractor> extractors,
        IEmbeddingService embeddingService,
        DocumentChunker chunker,
        IKnowledgeBaseRepository repository)
    {
        _extractors = extractors;
        _embeddingService = embeddingService;
        _chunker = chunker;
        _repository = repository;
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
        var chunkTexts = _chunker.Chunk(text);

        var chunks = new List<KnowledgeBaseChunk>();
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkTexts[i], ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Embedding = embedding,
            });
        }

        await _repository.AddChunksAsync(chunks, ct);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;
    }
}
