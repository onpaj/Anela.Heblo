using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.ClearFlagOverride;

internal sealed class ClearFlagOverrideHandler : IRequestHandler<ClearFlagOverrideRequest, ClearFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;

    public ClearFlagOverrideHandler(IFeatureFlagOverrideRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<ClearFlagOverrideResponse> Handle(
        ClearFlagOverrideRequest request, CancellationToken ct)
    {
        var deleted = await _repo.DeleteAsync(request.Key, ct);
        if (!deleted)
            return new ClearFlagOverrideResponse(ErrorCodes.ResourceNotFound);
        _cache.Remove(HebloFeatureProvider.CacheKey);
        return new ClearFlagOverrideResponse();
    }
}
