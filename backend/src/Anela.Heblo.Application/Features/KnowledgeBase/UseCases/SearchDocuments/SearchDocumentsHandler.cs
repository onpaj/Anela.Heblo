using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;

public class SearchDocumentsHandler : IRequestHandler<SearchDocumentsRequest, SearchDocumentsResponse>
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IKnowledgeBaseRepository _repository;
    private readonly KnowledgeBaseOptions _options;
    private readonly IChatClient _chatClient;

    public SearchDocumentsHandler(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IKnowledgeBaseRepository repository,
        IOptions<KnowledgeBaseOptions> options,
        IChatClient chatClient)
    {
        _embeddingGenerator = embeddingGenerator;
        _repository = repository;
        _options = options.Value;
        _chatClient = chatClient;
    }

    public async Task<SearchDocumentsResponse> Handle(
        SearchDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var queryToEmbed = _options.QueryExpansionEnabled
            ? await ExpandQueryAsync(request.Query, cancellationToken)
            : request.Query;

        var embeddings = await _embeddingGenerator.GenerateAsync(
            [queryToEmbed],
            cancellationToken: cancellationToken);
        var queryEmbedding = embeddings[0].Vector.ToArray();
        var results = await _repository.SearchSimilarAsync(queryEmbedding, request.TopK, cancellationToken);

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

    private async Task<string> ExpandQueryAsync(string query, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, _options.QueryExpansionPrompt + "\n" + query)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(response.Text) ? query : response.Text;
    }
}
