using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapHandler : IRequestHandler<RegenerateMindMapRequest, RegenerateMindMapResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public RegenerateMindMapHandler(IMindMapRepository repository, IBackgroundJobClient backgroundJobClient)
    {
        _repository = repository;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<RegenerateMindMapResponse> Handle(RegenerateMindMapRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new RegenerateMindMapResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new RegenerateMindMapResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        var hasPending = map.Meetings.Any(m => m.ProcessedAt == null);
        if (!hasPending)
        {
            map.Status = MindMapStatus.Idle;
            map.LastError = null;
            map.UpdatedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            return new RegenerateMindMapResponse();
        }

        map.Status = MindMapStatus.Updating;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        return new RegenerateMindMapResponse();
    }
}
