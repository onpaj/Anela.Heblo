using System.Diagnostics;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Pipeline;

/// <summary>
/// Persists a <see cref="RagInteractionLog"/> row for each Smartsupp draft-reply generation, capturing
/// the full RAG interaction for the eval dataset. Stamps the response with the log id so the frontend
/// can link the sent message and feedback back to this draft.
/// </summary>
public class DraftReplyLoggingBehavior : IPipelineBehavior<GenerateDraftReplyRequest, GenerateDraftReplyResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly IRagInteractionRecorder _recorder;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DraftReplyLoggingBehavior> _logger;

    public DraftReplyLoggingBehavior(
        IRagInteractionLogRepository repository,
        IRagInteractionRecorder recorder,
        ICurrentUserService currentUserService,
        ILogger<DraftReplyLoggingBehavior> logger)
    {
        _repository = repository;
        _recorder = recorder;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GenerateDraftReplyResponse> Handle(
        GenerateDraftReplyRequest request,
        RequestHandlerDelegate<GenerateDraftReplyResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        // Skip failed generations (conversation not found / empty / AI unavailable).
        if (!response.Success || !_recorder.HasInteraction)
            return response;

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var log = RagInteractionLogFactory.Build(
                _recorder,
                currentUser.Id,
                sw.ElapsedMilliseconds,
                DateTimeOffset.UtcNow);

            await _repository.SaveAsync(log, cancellationToken);
            response.Id = log.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write RAG interaction log for Smartsupp draft reply. Conversation: {ConversationId}", request.ConversationId);
        }

        return response;
    }
}
