using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>A <see cref="ScriptedAdReadSource"/> that also serves date ranges, recording each range asked for.</summary>
internal sealed class RangeAdReadSource : IAdPlatformReadSource, IAdPlatformRangeReadSource
{
    private readonly ScriptedAdReadSource _inner;

    public RangeAdReadSource(ScriptedAdReadSource inner) => _inner = inner;

    public AdPlatform Platform => _inner.Platform;
    public AdSourceCapabilities Capabilities => _inner.Capabilities;

    public List<(DateOnly From, DateOnly To)> FactRangeRequests { get; } = new();
    public List<(DateOnly From, DateOnly To)> SearchTermRangeRequests { get; } = new();
    public HashSet<DateOnly> FailRangesStartingOn { get; } = new();

    public Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct) => _inner.GetAccountsAsync(ct);

    public Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct) =>
        _inner.GetEntitiesAsync(accountExternalId, ct);

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct) =>
        _inner.GetDailyFactsAsync(accountExternalId, date, ct);

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct) =>
        _inner.GetSearchTermsAsync(accountExternalId, date, ct);

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct) =>
        _inner.GetChangeEventsAsync(accountExternalId, since, ct);

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        FactRangeRequests.Add((from, to));
        if (FailRangesStartingOn.Contains(from))
            throw new HttpRequestException($"{Platform}: facts unavailable");
        var rows = _inner.Facts.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.Date >= from && r.Date <= to).ToList() : new List<AdDailyFactRow>();
        return Task.FromResult<IReadOnlyList<AdDailyFactRow>>(rows);
    }

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        SearchTermRangeRequests.Add((from, to));
        var rows = _inner.SearchTerms.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.Date >= from && r.Date <= to).ToList() : new List<AdSearchTermRow>();
        return Task.FromResult<IReadOnlyList<AdSearchTermRow>>(rows);
    }
}
