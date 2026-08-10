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

        var previousStatus = map.Status;
        map.Status = MindMapStatus.Updating;
        map.LastError = null;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        }
        catch (Exception)
        {
            // Same compensation as AttachMeetingHandler: without this, a storage blip here
            // strands the map in Updating with nothing queued, and regenerate itself refuses
            // to help while Status == Updating.
            map.Status = previousStatus;
            map.UpdatedAt = DateTime.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);
            return new RegenerateMindMapResponse(ErrorCodes.InternalServerError);
        }

        return new RegenerateMindMapResponse();
    }
}
