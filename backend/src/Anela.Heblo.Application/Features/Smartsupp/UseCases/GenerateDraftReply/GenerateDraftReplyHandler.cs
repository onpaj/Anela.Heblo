using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
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
    private readonly ISmartsuppKnowledgeSource _knowledgeSource;
    private readonly IChatClient _chatClient;
    private readonly SmartsuppDraftReplyOptions _options;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRagInteractionRecorder _recorder;
    private readonly ILogger<GenerateDraftReplyHandler> _logger;

    public GenerateDraftReplyHandler(
        ISmartsuppRepository repository,
        ISmartsuppKnowledgeSource knowledgeSource,
        IChatClient chatClient,
        IOptions<SmartsuppDraftReplyOptions> options,
        ICurrentUserService currentUserService,
        IRagInteractionRecorder recorder,
        ILogger<GenerateDraftReplyHandler> logger)
    {
        _repository = repository;
        _knowledgeSource = knowledgeSource;
        _chatClient = chatClient;
        _options = options.Value;
        _currentUserService = currentUserService;
        _recorder = recorder;
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

        var chunks = await _knowledgeSource.SearchAsync(retrievalQuery, RetrievalTopK, cancellationToken);

        var context = chunks.Count != 0
            ? string.Join("\n\n---\n\n", chunks.Select(c => c.Content))
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

        var answer = response.Text ?? string.Empty;

        _recorder.RecordInteraction(
            RagFeature.SmartsuppDraftReply,
            retrievalQuery,
            systemPrompt,
            answer,
            conversationId: request.ConversationId,
            topic: topic);

        return new GenerateDraftReplyResponse
        {
            Answer = answer,
            Sources = chunks.Select(c => new DraftReplySource
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
