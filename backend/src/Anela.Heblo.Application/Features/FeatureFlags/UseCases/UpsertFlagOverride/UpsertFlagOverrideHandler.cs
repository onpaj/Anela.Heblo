using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

internal sealed class UpsertFlagOverrideHandler : IRequestHandler<UpsertFlagOverrideRequest, UpsertFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;

    public UpsertFlagOverrideHandler(IFeatureFlagOverrideRepository repo, IMemoryCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<UpsertFlagOverrideResponse> Handle(
        UpsertFlagOverrideRequest request, CancellationToken ct)
    {
        if (!FeatureFlagRegistry.ByKey.ContainsKey(request.Key))
            return new UpsertFlagOverrideResponse(ErrorCodes.ResourceNotFound);

        await _repo.UpsertAsync(request.Key, request.IsEnabled, request.UpdatedBy, ct);
        _cache.Remove(HebloFeatureProvider.CacheKey);
        return new UpsertFlagOverrideResponse();
    }
}
