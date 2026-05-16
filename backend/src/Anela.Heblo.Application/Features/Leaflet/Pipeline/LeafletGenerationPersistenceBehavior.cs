using System.Diagnostics;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.Pipeline;

public class LeafletGenerationPersistenceBehavior
    : IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>
{
    private readonly ILeafletGenerationRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<LeafletGenerationPersistenceBehavior> _logger;

    public LeafletGenerationPersistenceBehavior(
        ILeafletGenerationRepository repository,
        ICurrentUserService currentUserService,
        ILogger<LeafletGenerationPersistenceBehavior> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GenerateLeafletResponse> Handle(
        GenerateLeafletRequest request,
        RequestHandlerDelegate<GenerateLeafletResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (!response.Success)
            return response;

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var generation = new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = request.Topic,
                Audience = request.Audience.ToString(),
                Length = request.Length.ToString(),
                FinalMarkdown = response.Content,
                KbSourceCount = response.KbSourceCount,
                LeafletSourceCount = response.LeafletSourceCount,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTimeOffset.UtcNow,
                UserId = currentUser.Id,
            };

            await _repository.SaveGenerationAsync(generation, cancellationToken);
            response.Id = generation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log leaflet generation. Topic: {Topic}", request.Topic);
        }

        return response;
    }
}
