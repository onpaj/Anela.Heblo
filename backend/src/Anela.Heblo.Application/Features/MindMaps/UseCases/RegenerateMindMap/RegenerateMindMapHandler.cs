using Anela.Heblo.Application.Features.MindMaps.Infrastructure.Jobs;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapHandler : IRequestHandler<RegenerateMindMapRequest, RegenerateMindMapResponse>
{
    private readonly IMindMapRepository _repository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<RegenerateMindMapHandler> _logger;

    public RegenerateMindMapHandler(
        IMindMapRepository repository,
        IBackgroundJobClient backgroundJobClient,
        ILogger<RegenerateMindMapHandler> logger)
    {
        _repository = repository;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
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
        var previousLastError = map.LastError;
        map.Status = MindMapStatus.Updating;
        map.LastError = null;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            _backgroundJobClient.Enqueue<MindMapUpdateJob>(j => j.RunAsync(map.Id, CancellationToken.None));
        }
        catch (Exception ex)
        {
            // Same compensation as AttachMeetingHandler: without this, a storage blip here
            // strands the map in Updating with nothing queued, and regenerate itself refuses
            // to help while Status == Updating. Restore LastError too — otherwise a Failed
            // map's diagnostic message is destroyed even though the regenerate attempt itself
            // never got off the ground.
            _logger.LogError(ex,
                "Failed to enqueue update job for mind map {MindMapId} during regenerate; reverting status",
                map.Id);
            map.Status = previousStatus;
            map.LastError = previousLastError;
            map.UpdatedAt = DateTime.UtcNow;
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                // Don't let a failure to persist the revert mask the original enqueue
                // failure — the caller still gets a structured error either way.
                _logger.LogError(saveEx,
                    "Failed to revert mind map {MindMapId} status after enqueue failure during regenerate",
                    map.Id);
            }
            return new RegenerateMindMapResponse(ErrorCodes.InternalServerError);
        }

        return new RegenerateMindMapResponse();
    }
}
