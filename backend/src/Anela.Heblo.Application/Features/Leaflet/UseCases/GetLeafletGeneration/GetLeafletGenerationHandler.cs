using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;

public class GetLeafletGenerationHandler
    : IRequestHandler<GetLeafletGenerationRequest, GetLeafletGenerationResponse>
{
    private readonly ILeafletGenerationRepository _repository;

    public GetLeafletGenerationHandler(ILeafletGenerationRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetLeafletGenerationResponse> Handle(
        GetLeafletGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var generation = await _repository.GetGenerationByIdAsync(request.Id, cancellationToken);
        if (generation is null)
            return new GetLeafletGenerationResponse(ErrorCodes.LeafletFeedbackNotFound);

        return new GetLeafletGenerationResponse
        {
            Id = generation.Id,
            Topic = generation.Topic,
            Audience = generation.Audience,
            Length = generation.Length,
            FinalMarkdown = generation.FinalMarkdown,
            KbSourceCount = generation.KbSourceCount,
            LeafletSourceCount = generation.LeafletSourceCount,
            DurationMs = generation.DurationMs,
            CreatedAt = generation.CreatedAt,
            UserId = generation.UserId,
            PrecisionScore = generation.PrecisionScore,
            StyleScore = generation.StyleScore,
            FeedbackComment = generation.FeedbackComment,
        };
    }
}
