namespace Anela.Heblo.Application.Features.MarketingAds.Contracts;

/// <summary>
/// Optional extra for an <see cref="IAdPlatformReadSource"/> that can return many days in one call.
/// The backfill uses it to load history without spending one API call per day (Google's daily
/// operation quota cannot cover a year read day by day); sources without it are read day by day.
/// Both bounds are inclusive account-local dates; rows carry their own date.
/// </summary>
public interface IAdPlatformRangeReadSource
{
    Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly from, DateOnly to, CancellationToken ct);
}
