using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>Used when AdsDbContext is not registered. "Unknown" everywhere, so the limits engine denies.</summary>
public sealed class UnavailableAdsReadModel : IAdsReadModel
{
    private static readonly IReadOnlyDictionary<string, AdGroupOccupancy> NoOccupancy = new Dictionary<string, AdGroupOccupancy>();
    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    public Task<bool?> IsAccountManagedAsync(AdPlatform platform, string accountExternalId, CancellationToken cancellationToken) =>
        Task.FromResult<bool?>(null);

    public Task<IReadOnlyDictionary<string, AdGroupOccupancy>> GetAdGroupOccupancyAsync(AdPlatform platform, string accountExternalId,
        IReadOnlyCollection<string> adExternalIds, CancellationToken cancellationToken) => Task.FromResult(NoOccupancy);

    public Task<IReadOnlyDictionary<string, string>> GetEntityNamesAsync(AdPlatform platform, string accountExternalId,
        IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken) => Task.FromResult(NoNames);
}
