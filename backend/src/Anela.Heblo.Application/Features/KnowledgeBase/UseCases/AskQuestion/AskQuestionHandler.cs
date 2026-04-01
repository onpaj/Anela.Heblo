using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;
    private readonly IProductEnrichmentCache _enrichmentCache;

    public AskQuestionHandler(
        IMediator mediator,
        IChatClient chatClient,
        IOptions<KnowledgeBaseOptions> options,
        IProductEnrichmentCache enrichmentCache)
    {
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
        _enrichmentCache = enrichmentCache;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        if (!searchResult.Chunks.Any())
        {
            return new AskQuestionResponse
            {
                Answer = "V dostupných dokumentech jsem nenašla relevantní informaci k vaší otázce.",
                Sources = []
            };
        }

        var context = string.Join("\n\n---\n\n", searchResult.Chunks.Select(c => c.Content));

        var productLookup = await _enrichmentCache.GetProductLookupAsync(cancellationToken);
        var productTable = string.Join("\n", productLookup.Values.OrderBy(p => p.ProductCode).Select(p => $"{p.ProductCode} | {p.ProductName}"));

        var systemPrompt = _options.AskQuestionSystemPrompt
            .Replace("{context}", context)
            .Replace("{products}", productTable)
            .Replace("{query}", request.Question);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, request.Question)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var answer = response.Text ?? string.Empty;

        return new AskQuestionResponse
        {
            Answer = answer,
            Sources = searchResult.Chunks.Select(c => new SourceReference
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(200, c.Content.Length)],
                Score = c.Score
            }).ToList()
        };
    }
}
