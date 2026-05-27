using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackHandler
    : IRequestHandler<SubmitLeafletFeedbackRequest, SubmitLeafletFeedbackResponse>
{
    private readonly ILeafletGenerationRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitLeafletFeedbackHandler(
        ILeafletGenerationRepository repository,
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
        if (generation.UserId is null || currentUser.Id is null)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden,
                new() { { "generationId", request.GenerationId.ToString() } });

        if (generation.UserId != currentUser.Id)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden,
                new() { { "generationId", request.GenerationId.ToString() } });

        var updateResult = await _repository.UpdateFeedbackAsync(
            request.GenerationId,
            request.PrecisionScore,
            request.StyleScore,
            request.Comment,
            cancellationToken);

        return updateResult switch
        {
            UpdateFeedbackResult.Updated => new SubmitLeafletFeedbackResponse(),
            UpdateFeedbackResult.NotFound => new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackNotFound,
                new() { { "generationId", request.GenerationId.ToString() } }),
            UpdateFeedbackResult.AlreadySubmitted => new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackAlreadySubmitted,
                new() { { "generationId", request.GenerationId.ToString() } }),
            _ => throw new InvalidOperationException($"Unexpected UpdateFeedbackResult: {updateResult}"),
        };
    }
}
