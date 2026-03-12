using System.Diagnostics;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class QuestionLoggingBehavior : IPipelineBehavior<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<QuestionLoggingBehavior> _logger;

    public QuestionLoggingBehavior(
        IKnowledgeBaseRepository repository,
        ICurrentUserService currentUserService,
        ILogger<QuestionLoggingBehavior> logger)
    {
        _repository = repository;
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

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var log = new KnowledgeBaseQuestionLog
            {
                Id = Guid.NewGuid(),
                Question = request.Question,
                Answer = response.Answer,
                TopK = request.TopK,
                SourceCount = response.Sources.Count,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow,
                UserId = currentUser.Id
            };

            await _repository.SaveQuestionLogAsync(log, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write knowledge base question log. Question: {Question}", request.Question);
        }

        return response;
    }
}
