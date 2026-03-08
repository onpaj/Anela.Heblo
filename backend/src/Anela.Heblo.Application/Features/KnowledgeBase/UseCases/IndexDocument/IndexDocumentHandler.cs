using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentHandler : IRequestHandler<IndexDocumentRequest>
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IEmbeddingService _embeddingService;
    private readonly DocumentChunker _chunker;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ILogger<IndexDocumentHandler> _logger;

    public IndexDocumentHandler(
        IEnumerable<IDocumentTextExtractor> extractors,
        IEmbeddingService embeddingService,
        DocumentChunker chunker,
        IKnowledgeBaseRepository repository,
        ILogger<IndexDocumentHandler>? logger = null)
    {
        _extractors = extractors;
        _embeddingService = embeddingService;
        _chunker = chunker;
        _repository = repository;
        _logger = logger ?? NullLogger<IndexDocumentHandler>.Instance;
    }

    public async Task Handle(IndexDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Indexing document {Filename} from {SourcePath}", request.Filename, request.SourcePath);

        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
            ?? throw new NotSupportedException($"Content type '{request.ContentType}' is not supported.");

        var document = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = request.ContentType,
            ContentHash = request.ContentHash,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddDocumentAsync(document, cancellationToken);

        var text = await extractor.ExtractTextAsync(request.Content, cancellationToken);
        var chunkTexts = _chunker.Chunk(text);

        var chunks = new List<KnowledgeBaseChunk>();

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkTexts[i], cancellationToken);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Embedding = embedding
            });
        }

        await _repository.AddChunksAsync(chunks, cancellationToken);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Indexed {ChunkCount} chunks for {Filename}", chunks.Count, request.Filename);
    }
}
