using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;

/// <summary>Read side of the C1 <c>ads</c> schema, as far as the limits engine and the diff renderer need it.</summary>
public interface IAdsReadModel
{
    /// <summary>null = account unknown to Heblo (or the ads DB is not configured).</summary>
    Task<bool?> IsAccountManagedAsync(AdPlatform platform, string accountExternalId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, AdGroupOccupancy>> GetAdGroupOccupancyAsync(AdPlatform platform, string accountExternalId, IReadOnlyCollection<string> adExternalIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>> GetEntityNamesAsync(AdPlatform platform, string accountExternalId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);
}
