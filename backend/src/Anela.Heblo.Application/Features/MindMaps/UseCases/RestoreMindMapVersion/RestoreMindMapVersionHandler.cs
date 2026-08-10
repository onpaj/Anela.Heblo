using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;

public class RestoreMindMapVersionHandler : IRequestHandler<RestoreMindMapVersionRequest, RestoreMindMapVersionResponse>
{
    private readonly IMindMapRepository _repository;

    public RestoreMindMapVersionHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<RestoreMindMapVersionResponse> Handle(RestoreMindMapVersionRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.ResourceNotFound);
        }

        if (map.Status == MindMapStatus.Updating)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.MindMapUpdateInProgress);
        }

        var version = map.Versions.FirstOrDefault(v => v.VersionNumber == request.VersionNumber);
        if (version is null)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.ResourceNotFound);
        }

        // Safe: `version` above was found inside map.Versions, so the collection is non-empty here.
        var nextVersionNumber = map.Versions.Max(v => v.VersionNumber) + 1;
        map.Versions.Add(new MindMapVersion
        {
            Id = Guid.NewGuid(),
            MindMapId = map.Id,
            VersionNumber = nextVersionNumber,
            Json = map.CurrentJson,
            CreatedAt = DateTime.UtcNow,
            TriggerMeetingId = null
        });
        map.CurrentJson = version.Json;
        map.UpdatedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);

        return new RestoreMindMapVersionResponse { DocumentJson = map.CurrentJson };
    }
}
