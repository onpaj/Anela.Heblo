namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Read side of one ad platform (spec 4.2). Rows carry platform external ids only. Unsupported
/// capabilities return an empty list, never throw. Transport/auth errors throw; the core catches them
/// per source so one failing platform never blocks the others. A platform registers its source only
/// when <see cref="AdSettingsGuard.IsConfigured"/> holds for all its settings.
/// </summary>
public interface IAdPlatformReadSource
{
    AdPlatform Platform { get; }
    AdSourceCapabilities Capabilities { get; }
    Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct);
    Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct);
    Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct);
}
