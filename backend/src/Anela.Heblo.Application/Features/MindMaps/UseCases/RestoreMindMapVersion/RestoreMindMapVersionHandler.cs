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

        // Targeted single-row fetch — map.Versions is not loaded by GetByIdAsync (that
        // would pull every version's full Json blob for a read path that only ever needs
        // one row here).
        var version = await _repository.GetVersionAsync(map.Id, request.VersionNumber, cancellationToken);
        if (version is null)
        {
            return new RestoreMindMapVersionResponse(ErrorCodes.ResourceNotFound);
        }

        var nextVersionNumber = await _repository.GetNextVersionNumberAsync(map.Id, cancellationToken);
        map.Versions.Add(new MindMapVersion
        {
            // No explicit Id: EF marks an entity added to a tracked parent's navigation
            // collection as Modified when its key already has a value, which issues an
            // UPDATE against a row that does not exist yet. Letting EF generate the key
            // keeps the entity Added so it is INSERTed.
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
