using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackHandler
    : IRequestHandler<SubmitLeafletFeedbackRequest, SubmitLeafletFeedbackResponse>
{
    private readonly ILeafletRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitLeafletFeedbackHandler(
        ILeafletRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitLeafletFeedbackResponse> Handle(
        SubmitLeafletFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var generation = await _repository.GetGenerationByIdAsync(request.GenerationId, cancellationToken);
        if (generation is null)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackNotFound,
                new() { { "generationId", request.GenerationId.ToString() } });

        var currentUser = _currentUserService.GetCurrentUser();
        if (generation.UserId != currentUser.Id)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden,
                new() { { "generationId", request.GenerationId.ToString() } });

        if (generation.PrecisionScore is not null || generation.StyleScore is not null)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackAlreadySubmitted,
                new() { { "generationId", request.GenerationId.ToString() } });

        generation.PrecisionScore = request.PrecisionScore;
        generation.StyleScore = request.StyleScore;
        generation.FeedbackComment = request.Comment;

        await _repository.SaveChangesAsync(cancellationToken);
        return new SubmitLeafletFeedbackResponse();
    }
}
