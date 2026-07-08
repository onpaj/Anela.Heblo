using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SubmitDraftReplyFeedback;

public class SubmitDraftReplyFeedbackHandler
    : IRequestHandler<SubmitDraftReplyFeedbackRequest, SubmitDraftReplyFeedbackResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitDraftReplyFeedbackHandler(
        IRagInteractionLogRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitDraftReplyFeedbackResponse> Handle(
        SubmitDraftReplyFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var log = await _repository.GetByIdAsync(request.LogId, cancellationToken);
        if (log is null || log.Feature != RagFeature.SmartsuppDraftReply)
        {
            return new SubmitDraftReplyFeedbackResponse(
                ErrorCodes.SmartsuppDraftReplyFeedbackLogNotFound,
                new Dictionary<string, string> { { "logId", request.LogId.ToString() } });
        }

        var currentUser = _currentUserService.GetCurrentUser();
        if (log.UserId != currentUser.Id)
        {
            return new SubmitDraftReplyFeedbackResponse(
                ErrorCodes.Forbidden,
                new Dictionary<string, string> { { "logId", request.LogId.ToString() } });
        }

        if (log.PrecisionScore is not null || log.StyleScore is not null)
        {
            return new SubmitDraftReplyFeedbackResponse(
                ErrorCodes.SmartsuppDraftReplyFeedbackAlreadySubmitted,
                new Dictionary<string, string> { { "logId", request.LogId.ToString() } });
        }

        log.PrecisionScore = request.PrecisionScore;
        log.StyleScore = request.StyleScore;
        log.FeedbackComment = request.Comment;

        await _repository.SaveChangesAsync(cancellationToken);

        return new SubmitDraftReplyFeedbackResponse();
    }
}
