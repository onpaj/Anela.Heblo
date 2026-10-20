using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

/// <summary>Registered when AdsDbContext is not. Handlers check IsAvailable first and never call the methods.</summary>
public sealed class UnavailableAdsReadRepository : IAdsReadRepository
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct) => throw Unavailable();
    public Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct) => throw Unavailable();
    public Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct) => throw Unavailable();
    public Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct) => throw Unavailable();
    public Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct) => throw Unavailable();

    private static InvalidOperationException Unavailable() =>
        new("The ads database is not configured (AdsDatabase:ConnectionString).");
}
