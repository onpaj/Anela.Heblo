using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SubmitFeedback;

public class SubmitFeedbackHandler : IRequestHandler<SubmitFeedbackRequest, SubmitFeedbackResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitFeedbackHandler(
        IRagInteractionLogRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitFeedbackResponse> Handle(
        SubmitFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var log = await _repository.GetByIdAsync(request.LogId, cancellationToken);
        if (log is null)
        {
            return new SubmitFeedbackResponse(ErrorCodes.KnowledgeBaseFeedbackLogNotFound, new Dictionary<string, string>
            {
                { "logId", request.LogId.ToString() }
            });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        if (log.UserId != currentUser.Id)
        {
            return new SubmitFeedbackResponse(ErrorCodes.Forbidden, new Dictionary<string, string>
            {
                { "logId", request.LogId.ToString() }
            });
        }

        if (log.PrecisionScore is not null || log.StyleScore is not null)
        {
            return new SubmitFeedbackResponse(ErrorCodes.KnowledgeBaseFeedbackAlreadySubmitted, new Dictionary<string, string>
            {
                { "logId", request.LogId.ToString() }
            });
        }

        log.PrecisionScore = request.PrecisionScore;
        log.StyleScore = request.StyleScore;
        log.FeedbackComment = request.Comment;

        await _repository.SaveChangesAsync(cancellationToken);

        return new SubmitFeedbackResponse();
    }
}
