using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentIndexingService : IDocumentIndexingService
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ChatTranscriptPreprocessor _preprocessor;
    private readonly IEnumerable<IIndexingStrategy> _strategies;

    public DocumentIndexingService(
        IEnumerable<IDocumentTextExtractor> extractors,
        IKnowledgeBaseRepository repository,
        ChatTranscriptPreprocessor preprocessor,
        IEnumerable<IIndexingStrategy> strategies)
    {
        _extractors = extractors;
        _repository = repository;
        _preprocessor = preprocessor;
        _strategies = strategies;
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

        var strategy = _strategies.FirstOrDefault(s => s.Supports(document.DocumentType))
            ?? throw new NotSupportedException($"No indexing strategy for DocumentType '{document.DocumentType}'.");

        var chunks = await strategy.CreateChunksAsync(text, document.Id, ct);
        await _repository.AddChunksAsync(chunks, ct);

        // Caller is responsible for persisting this entity change (e.g. _repository.UpdateAsync or EF change tracking).
        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;
    }
}
