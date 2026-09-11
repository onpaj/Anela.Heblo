using Anela.Heblo.Application.Features.FeatureFlags.Infrastructure;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Features.FeatureFlags.UseCases.UpsertFlagOverride;

internal sealed class UpsertFlagOverrideHandler : IRequestHandler<UpsertFlagOverrideRequest, UpsertFlagOverrideResponse>
{
    private readonly IFeatureFlagOverrideRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly ICurrentUserService _currentUserService;

    public UpsertFlagOverrideHandler(
        IFeatureFlagOverrideRepository repo,
        IMemoryCache cache,
        ICurrentUserService currentUserService)
    {
        _repo = repo;
        _cache = cache;
        _currentUserService = currentUserService;
    }

    public async Task<UpsertFlagOverrideResponse> Handle(
        UpsertFlagOverrideRequest request, CancellationToken ct)
    {
        if (!FeatureFlagRegistry.ByKey.ContainsKey(request.Key))
            return new UpsertFlagOverrideResponse(ErrorCodes.ResourceNotFound);

        var updatedBy = _currentUserService.GetCurrentUser().GetDisplayName();
        await _repo.UpsertAsync(request.Key, request.IsEnabled, updatedBy, ct);
        _cache.Remove(HebloFeatureProvider.CacheKey);
        return new UpsertFlagOverrideResponse();
    }
}
