using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;
    private readonly IProductEnrichmentCache _enrichmentCache;
    private readonly ILogger<AskQuestionHandler> _logger;

    public AskQuestionHandler(
        IMediator mediator,
        IChatClient chatClient,
        IOptions<KnowledgeBaseOptions> options,
        IProductEnrichmentCache enrichmentCache,
        ILogger<AskQuestionHandler> logger)
    {
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
        _enrichmentCache = enrichmentCache;
        _logger = logger;
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

        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "AI service unavailable while processing KnowledgeBase/Ask");
            return new AskQuestionResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.KnowledgeBaseAiUnavailable
            };
        }

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
