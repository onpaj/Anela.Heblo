using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Tests.Features.MarketingAds.TestSupport;

/// <summary>
/// Scripted IAdPlatformReadSource used by every sync test. C1's FakeAdPlatformReadSource only has one
/// global FailWith, so it cannot fail one stream of one account while the others succeed.
/// Records what it was asked for.
/// </summary>
internal sealed class ScriptedAdReadSource : IAdPlatformReadSource
{
    public ScriptedAdReadSource(AdPlatform platform, bool searchTerms = true, bool changeLog = false, TimeSpan? changeLogMaxAge = null)
    {
        Platform = platform;
        Capabilities = new AdSourceCapabilities(searchTerms, changeLog, changeLogMaxAge);
    }

    public AdPlatform Platform { get; }
    public AdSourceCapabilities Capabilities { get; }

    public List<AdAccountSnapshot> Accounts { get; } = new();
    public Dictionary<string, List<AdEntitySnapshot>> Entities { get; } = new();
    public Dictionary<string, List<AdDailyFactRow>> Facts { get; } = new();
    public Dictionary<string, List<AdSearchTermRow>> SearchTerms { get; } = new();
    public Dictionary<string, List<AdChangeEventRow>> Changes { get; } = new();

    public bool FailAccounts { get; set; }
    public HashSet<string> FailFactsFor { get; } = new();
    public HashSet<string> FailChangesFor { get; } = new();

    public List<(string Account, DateOnly Date)> FactRequests { get; } = new();
    public List<(string Account, DateOnly Date)> SearchTermRequests { get; } = new();
    public List<(string Account, DateTimeOffset Since)> ChangeRequests { get; } = new();

    public Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct) =>
        FailAccounts
            ? throw new HttpRequestException($"{Platform}: accounts unavailable")
            : Task.FromResult<IReadOnlyList<AdAccountSnapshot>>(Accounts.ToList());

    public Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdEntitySnapshot>>(
            Entities.TryGetValue(accountExternalId, out var list) ? list.ToList() : new List<AdEntitySnapshot>());

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        FactRequests.Add((accountExternalId, date));
        if (FailFactsFor.Contains(accountExternalId))
            throw new HttpRequestException($"{Platform}: facts unavailable");
        var rows = Facts.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.Date == date).ToList() : new List<AdDailyFactRow>();
        return Task.FromResult<IReadOnlyList<AdDailyFactRow>>(rows);
    }

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        SearchTermRequests.Add((accountExternalId, date));
        var rows = SearchTerms.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.Date == date).ToList() : new List<AdSearchTermRow>();
        return Task.FromResult<IReadOnlyList<AdSearchTermRow>>(rows);
    }

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct)
    {
        ChangeRequests.Add((accountExternalId, since));
        if (FailChangesFor.Contains(accountExternalId))
            throw new HttpRequestException($"{Platform}: change history unavailable");
        var rows = Changes.TryGetValue(accountExternalId, out var list) ? list.Where(r => r.OccurredAt >= since).ToList() : new List<AdChangeEventRow>();
        return Task.FromResult<IReadOnlyList<AdChangeEventRow>>(rows);
    }
}
