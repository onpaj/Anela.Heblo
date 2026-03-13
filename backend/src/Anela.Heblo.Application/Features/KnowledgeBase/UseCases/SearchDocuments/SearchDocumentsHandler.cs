using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;

public class SearchDocumentsHandler : IRequestHandler<SearchDocumentsRequest, SearchDocumentsResponse>
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IKnowledgeBaseRepository _repository;

    public SearchDocumentsHandler(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IKnowledgeBaseRepository repository)
    {
        _embeddingGenerator = embeddingGenerator;
        _repository = repository;
    }

    public async Task<SearchDocumentsResponse> Handle(
        SearchDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync(
            [request.Query],
            cancellationToken: cancellationToken);
        var queryEmbedding = embeddings[0].Vector.ToArray();
        var results = await _repository.SearchSimilarAsync(queryEmbedding, request.TopK, cancellationToken);

        return new SearchDocumentsResponse
        {
            Chunks = results.Select(r => new ChunkResult
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
