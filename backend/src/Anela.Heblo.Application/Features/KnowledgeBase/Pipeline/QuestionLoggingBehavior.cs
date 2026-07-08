using System.Diagnostics;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

/// <summary>
/// Persists a <see cref="RagInteractionLog"/> row for each KnowledgeBase Ask, capturing the full
/// RAG interaction (question, retrieved chunks + scores, expanded query, rendered prompt, answer) for
/// the eval dataset. Reads the retrieval + prompt artifacts from the scoped
/// <see cref="IRagInteractionRecorder"/> populated by the handlers.
/// </summary>
public class QuestionLoggingBehavior : IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly IRagInteractionRecorder _recorder;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<QuestionLoggingBehavior> _logger;

    public QuestionLoggingBehavior(
        IRagInteractionLogRepository repository,
        IRagInteractionRecorder recorder,
        ICurrentUserService currentUserService,
        ILogger<QuestionLoggingBehavior> logger)
    {
        _repository = repository;
        _recorder = recorder;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        RequestHandlerDelegate<AskQuestionResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        // Skip AI-unavailable / failed responses — they are not useful eval rows.
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
            _logger.LogError(ex, "Failed to write RAG interaction log for KnowledgeBase Ask. Question: {Question}", request.Question);
        }

        return response;
    }
}
