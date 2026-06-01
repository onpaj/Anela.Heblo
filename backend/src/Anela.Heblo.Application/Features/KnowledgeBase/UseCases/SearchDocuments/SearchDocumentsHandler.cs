using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;

public class SearchDocumentsHandler : IRequestHandler<SearchDocumentsRequest, SearchDocumentsResponse>
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly KnowledgeBaseOptions _options;
    private readonly IRagQueryExpander _expander;
    private readonly ILogger<SearchDocumentsHandler> _logger;

    public SearchDocumentsHandler(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IKnowledgeBaseRepository repository,
        IOptions<KnowledgeBaseOptions> options,
        IRagQueryExpander expander,
        ILogger<SearchDocumentsHandler> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _repository = repository;
        _options = options.Value;
        _expander = expander;
        _logger = logger;
    }

    public async Task<SearchDocumentsResponse> Handle(
        SearchDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var queryToEmbed = await _expander.ExpandAsync(request.Query, _options.ToExpansionConfig(), cancellationToken);

        float[] queryEmbedding;
        try
        {
            var embeddings = await _embeddingGenerator.GenerateAsync(
                [queryToEmbed],
                cancellationToken: cancellationToken);
            queryEmbedding = embeddings[0].Vector.ToArray();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Transient embedding failure for query '{Query}' — exceptionType: {ExceptionType}, innerExceptionType: {InnerExceptionType}",
                request.Query,
                ex.GetType().Name,
                ex.InnerException?.GetType().Name ?? "none");
            return new SearchDocumentsResponse();
        }

        List<(KnowledgeBaseChunk Chunk, double Score)> results;
        try
        {
            results = await _repository.SearchSimilarAsync(queryEmbedding, request.TopK, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Transient vector DB failure during SearchSimilarAsync for query '{Query}' — exceptionType: {ExceptionType}, innerExceptionType: {InnerExceptionType}",
                request.Query,
                ex.GetType().Name,
                ex.InnerException?.GetType().Name ?? "none");
            return new SearchDocumentsResponse();
        }

        var above = results.Where(r => r.Score >= _options.MinSimilarityScore).ToList();
        var belowCount = results.Count - above.Count;

        return new SearchDocumentsResponse
        {
            BelowThresholdCount = belowCount,
            Chunks = above.Select(r => new ChunkResult
            {
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId,
                Content = r.Chunk.Content,
                Score = r.Score,
                SourceFilename = r.Chunk.Document.Filename,
                SourcePath = r.Chunk.Document.SourcePath
            }).ToList()
        };
    }
}
