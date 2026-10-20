using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Reporting;

public interface IAdsReadRepository
{
    /// <summary>False when AdsDbContext is not configured in this environment.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<AdAccountDto>> ListAccountsAsync(AdPlatform? platform, bool includeUnmanaged, CancellationToken ct);

    /// <summary>Null when the account is unknown.</summary>
    Task<AdPerformancePage?> GetPerformanceAsync(AdPerformanceQuery query, CancellationToken ct);

    /// <summary>Null when the account is unknown.</summary>
    Task<IReadOnlyList<AdSearchTermRowDto>?> GetSearchTermsAsync(AdSearchTermQuery query, CancellationToken ct);

    /// <summary>Null when ads.v_ads_blended_daily does not exist (views script not run, or no shoptet_raw).</summary>
    Task<IReadOnlyList<AdBlendedDayRow>?> GetBlendedDailyAsync(DateOnly from, DateOnly to, CancellationToken ct);

    Task<AdChangeEventPage> GetChangeEventsAsync(AdChangeEventQuery query, CancellationToken ct);
}
