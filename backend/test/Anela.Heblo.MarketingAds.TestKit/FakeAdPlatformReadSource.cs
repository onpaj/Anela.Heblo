using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.MarketingAds.TestKit;

/// <summary>
/// In-memory IAdPlatformReadSource so the core (C2 sync, C3/C4 handlers) is built and tested with no
/// platform present. Data is settable per account; disabled capabilities return empty lists like a
/// real source; <see cref="FailWith"/> simulates a transport/auth failure; <see cref="Calls"/>
/// records every request (recorded before a simulated failure is thrown).
/// </summary>
public sealed class FakeAdPlatformReadSource : IAdPlatformReadSource
{
    public const string SampleCampaignExternalId = "campaign-1";
    public const string SampleAdGroupExternalId = "adgroup-1";
    public const string SampleKeywordExternalId = "adgroup-1~keyword-1";
    public const string SampleAdExternalId = "adgroup-1~ad-1";
    public const string SampleCurrency = "CZK";

    public static readonly AdSourceCapabilities FullCapabilities =
        new(SearchTerms: true, ChangeLog: true, ChangeLogMaxAge: TimeSpan.FromDays(30));

    private readonly List<AdAccountSnapshot> _accounts = [];
    private readonly Dictionary<string, List<AdEntitySnapshot>> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdDailyFactRow>> _dailyFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdSearchTermRow>> _searchTerms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<AdChangeEventRow>> _changeEvents = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];
    private Exception? _failure;

    public FakeAdPlatformReadSource(AdPlatform platform, AdSourceCapabilities? capabilities = null)
    {
        Platform = platform;
        Capabilities = capabilities ?? FullCapabilities;
    }

    public AdPlatform Platform { get; }
    public AdSourceCapabilities Capabilities { get; }
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>
    /// One account with campaign → ad group → (keyword, ad), facts at campaign and ad level for
    /// <paramref name="date"/>, one search term (when supported) and one change event at 09:00 UTC.
    /// </summary>
    public static FakeAdPlatformReadSource CreateSample(
        AdPlatform platform, string accountExternalId, DateOnly date, AdSourceCapabilities? capabilities = null)
    {
        var noAttributes = new Dictionary<string, string?>();
        var source = new FakeAdPlatformReadSource(platform, capabilities)
            .WithAccount(new AdAccountSnapshot(accountExternalId, "Anela sample account", SampleCurrency, "Europe/Prague"))
            .WithEntities(accountExternalId,
                new AdEntitySnapshot(AdEntityLevel.Campaign, SampleCampaignExternalId, null, null, "Brand",
                    AdEntityStatus.Enabled, new Dictionary<string, string?> { ["dailyBudget"] = "500" }),
                new AdEntitySnapshot(AdEntityLevel.AdGroup, SampleAdGroupExternalId, AdEntityLevel.Campaign,
                    SampleCampaignExternalId, "Pleťové krémy", AdEntityStatus.Enabled, noAttributes),
                new AdEntitySnapshot(AdEntityLevel.Keyword, SampleKeywordExternalId, AdEntityLevel.AdGroup,
                    SampleAdGroupExternalId, "pleťový krém", AdEntityStatus.Enabled,
                    new Dictionary<string, string?> { ["text"] = "pleťový krém", ["matchType"] = nameof(KeywordMatchType.Phrase) }),
                new AdEntitySnapshot(AdEntityLevel.Ad, SampleAdExternalId, AdEntityLevel.AdGroup,
                    SampleAdGroupExternalId, "Responsive ad 1", AdEntityStatus.Enabled, noAttributes))
            .WithDailyFacts(accountExternalId,
                new AdDailyFactRow(AdEntityLevel.Campaign, SampleCampaignExternalId, date, 1200, 48, 312.50m, 3m, 2150m, SampleCurrency),
                new AdDailyFactRow(AdEntityLevel.Ad, SampleAdExternalId, date, 1200, 48, 312.50m, 3m, 2150m, SampleCurrency))
            .WithSearchTerms(accountExternalId,
                new AdSearchTermRow(SampleAdGroupExternalId, date, "krém na obličej", KeywordMatchType.Phrase,
                    300, 12, 80.10m, 1m, 640m, SampleCurrency))
            .WithChangeEvents(accountExternalId,
                new AdChangeEventRow("change-1", new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
                    "agency@example.com", AdChangeActorKind.User, AdEntityLevel.Ad, SampleAdExternalId, "StatusChanged",
                    "{\"status\":\"Enabled\"}", "{\"status\":\"Paused\"}"));
        return source;
    }

    public FakeAdPlatformReadSource WithAccount(AdAccountSnapshot account)
    {
        _accounts.Add(account);
        return this;
    }

    public FakeAdPlatformReadSource WithEntities(string accountExternalId, params AdEntitySnapshot[] entities) =>
        AddTo(_entities, accountExternalId, entities);

    public FakeAdPlatformReadSource WithDailyFacts(string accountExternalId, params AdDailyFactRow[] rows) =>
        AddTo(_dailyFacts, accountExternalId, rows);

    public FakeAdPlatformReadSource WithSearchTerms(string accountExternalId, params AdSearchTermRow[] rows) =>
        AddTo(_searchTerms, accountExternalId, rows);

    public FakeAdPlatformReadSource WithChangeEvents(string accountExternalId, params AdChangeEventRow[] rows) =>
        AddTo(_changeEvents, accountExternalId, rows);

    public FakeAdPlatformReadSource FailWith(Exception failure)
    {
        _failure = failure;
        return this;
    }

    public Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct)
    {
        Record(ct, "GetAccounts");
        return Result(_accounts);
    }

    public Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct)
    {
        Record(ct, $"GetEntities:{accountExternalId}");
        return Result(RowsOf(_entities, accountExternalId));
    }

    public Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        Record(ct, $"GetDailyFacts:{accountExternalId}:{date:yyyy-MM-dd}");
        return Result(RowsOf(_dailyFacts, accountExternalId).Where(r => r.Date == date));
    }

    public Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(string accountExternalId, DateOnly date, CancellationToken ct)
    {
        Record(ct, $"GetSearchTerms:{accountExternalId}:{date:yyyy-MM-dd}");
        return Capabilities.SearchTerms
            ? Result(RowsOf(_searchTerms, accountExternalId).Where(r => r.Date == date))
            : Result(Enumerable.Empty<AdSearchTermRow>());
    }

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct)
    {
        Record(ct, $"GetChangeEvents:{accountExternalId}:{since:O}");
        return Capabilities.ChangeLog
            ? Result(RowsOf(_changeEvents, accountExternalId).Where(r => r.OccurredAt >= since))
            : Result(Enumerable.Empty<AdChangeEventRow>());
    }

    private FakeAdPlatformReadSource AddTo<T>(Dictionary<string, List<T>> store, string accountExternalId, T[] rows)
    {
        if (!store.TryGetValue(accountExternalId, out var bucket))
        {
            bucket = [];
            store[accountExternalId] = bucket;
        }

        bucket.AddRange(rows);
        return this;
    }

    private void Record(CancellationToken ct, string call)
    {
        ct.ThrowIfCancellationRequested();
        _calls.Add(call);
        if (_failure is not null)
            throw _failure;
    }

    private static IEnumerable<T> RowsOf<T>(Dictionary<string, List<T>> store, string accountExternalId) =>
        store.TryGetValue(accountExternalId, out var rows) ? rows : Enumerable.Empty<T>();

    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> rows) =>
        Task.FromResult<IReadOnlyList<T>>(rows.ToList());
}
