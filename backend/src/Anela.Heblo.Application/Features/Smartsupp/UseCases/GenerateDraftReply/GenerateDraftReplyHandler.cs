using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

public class GenerateDraftReplyHandler
    : IRequestHandler<GenerateDraftReplyRequest, GenerateDraftReplyResponse>
{
    private const int RetrievalTopK = 5;
    private const int MaxExcerptLength = 200;

    // Keep the retrieval query within SearchDocumentsRequest's MaxLength(2000) constraint.
    private const int MaxRetrievalQueryLength = 2000;
    private const string NoContextPlaceholder = "(žádný relevantní kontext nebyl nalezen)";
    private const string NoTopicPlaceholder = "(neuvedeno)";

    private readonly ISmartsuppRepository _repository;
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly SmartsuppDraftReplyOptions _options;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GenerateDraftReplyHandler> _logger;

    public GenerateDraftReplyHandler(
        ISmartsuppRepository repository,
        IMediator mediator,
        IChatClient chatClient,
        IOptions<SmartsuppDraftReplyOptions> options,
        ICurrentUserService currentUserService,
        ILogger<GenerateDraftReplyHandler> logger)
    {
        _repository = repository;
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GenerateDraftReplyResponse> Handle(
        GenerateDraftReplyRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppConversationNotFound);

        var topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim();
        var retrievalQuery = topic
            ?? ConversationTranscriptBuilder.LastContactMessages(conversation.Messages);
        if (string.IsNullOrWhiteSpace(retrievalQuery))
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppConversationEmpty);

        if (retrievalQuery.Length > MaxRetrievalQueryLength)
            retrievalQuery = retrievalQuery[..MaxRetrievalQueryLength];

        var transcript = ConversationTranscriptBuilder.Build(conversation.Messages);

        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = retrievalQuery, TopK = RetrievalTopK },
            cancellationToken);

        var context = searchResult.Chunks.Count != 0
            ? string.Join("\n\n---\n\n", searchResult.Chunks.Select(c => c.Content))
            : NoContextPlaceholder;

        var agentName = SmartsuppNameHelper.ExtractFirstName(_currentUserService.GetCurrentUser().Name);

        var systemPrompt = _options.DraftReplySystemPrompt
            .Replace("{agent_name}", agentName)
            .Replace("{transcript}", transcript)
            .Replace("{context}", context)
            .Replace("{topic}", topic ?? NoTopicPlaceholder);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, "Napiš návrh odpovědi agenta na poslední zprávu zákazníka."),
        };

        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                       or ObjectDisposedException
                                       || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken))
        {
            _logger.LogWarning(ex, "AI service unavailable while generating Smartsupp draft reply");
            return new GenerateDraftReplyResponse(ErrorCodes.SmartsuppDraftReplyAiUnavailable);
        }

        return new GenerateDraftReplyResponse
        {
            Answer = response.Text ?? string.Empty,
            Sources = searchResult.Chunks.Select(c => new DraftReplySource
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(MaxExcerptLength, c.Content.Length)],
                Score = c.Score,
            }).ToList(),
        };
    }
}
