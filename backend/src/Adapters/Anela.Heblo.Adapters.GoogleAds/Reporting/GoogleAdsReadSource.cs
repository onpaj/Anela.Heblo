using System.Text.Json;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Reporting;

/// <summary>
/// Google Ads side of the MarketingAds backbone. One configured client account; every query goes
/// through <see cref="IGoogleAdsApiClient"/>. Transport/auth errors propagate — the core catches per source.
/// </summary>
internal sealed class GoogleAdsReadSource : IAdPlatformReadSource
{
    internal static readonly TimeSpan ChangeLogMaxAge = TimeSpan.FromDays(30);

    private static readonly (GoogleAdsQuery Query, Func<JsonElement, AdEntitySnapshot> Map)[] EntityQueries =
    {
        (GoogleAdsQueries.Campaigns, GoogleAdsEntityMapper.Campaign),
        (GoogleAdsQueries.AdGroups, GoogleAdsEntityMapper.AdGroup),
        (GoogleAdsQueries.Keywords, GoogleAdsEntityMapper.Keyword),
        (GoogleAdsQueries.AdGroupNegativeKeywords, GoogleAdsEntityMapper.AdGroupNegativeKeyword),
        (GoogleAdsQueries.CampaignNegativeKeywords, GoogleAdsEntityMapper.CampaignNegativeKeyword),
        (GoogleAdsQueries.Ads, GoogleAdsEntityMapper.Ad),
    };

    private readonly IGoogleAdsApiClient _api;
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GoogleAdsReadSource> _logger;
    private readonly Dictionary<string, GoogleAdsCustomer> _customers = new(StringComparer.Ordinal);

    public GoogleAdsReadSource(
        IGoogleAdsApiClient api,
        IOptionsMonitor<GoogleAdsSettings> settings,
        TimeProvider timeProvider,
        ILogger<GoogleAdsReadSource> logger)
    {
        _api = api;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public AdPlatform Platform => AdPlatform.GoogleAds;

    public AdSourceCapabilities Capabilities { get; } =
        new(SearchTerms: true, ChangeLog: true, ChangeLogMaxAge: ChangeLogMaxAge);

    public async Task<IReadOnlyList<AdAccountSnapshot>> GetAccountsAsync(CancellationToken ct)
    {
        var customer = await GetCustomerAsync(ConfiguredCustomerId(), ct);
        return new[] { new AdAccountSnapshot(customer.Id, customer.Name, customer.Currency, customer.TimeZoneId) };
    }

    public async Task<IReadOnlyList<AdEntitySnapshot>> GetEntitiesAsync(string accountExternalId, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var entities = new List<AdEntitySnapshot>();
        foreach (var (query, map) in EntityQueries)
        {
            var rows = await _api.SearchAsync(customerId, query, ct);
            entities.AddRange(rows.Select(map));
        }
        return entities;
    }

    public async Task<IReadOnlyList<AdDailyFactRow>> GetDailyFactsAsync(
        string accountExternalId, DateOnly date, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var currency = (await GetCustomerAsync(customerId, ct)).Currency;
        var facts = new List<AdDailyFactRow>();
        foreach (var (level, query) in GoogleAdsQueries.FactQueries(date))
        {
            var rows = await _api.SearchAsync(customerId, query, ct);
            facts.AddRange(rows.Select(row => GoogleAdsFactMapper.Fact(level, row, currency)));
        }
        return facts;
    }

    public async Task<IReadOnlyList<AdSearchTermRow>> GetSearchTermsAsync(
        string accountExternalId, DateOnly date, CancellationToken ct)
    {
        var customerId = RequireConfiguredAccount(accountExternalId);
        var currency = (await GetCustomerAsync(customerId, ct)).Currency;
        var rows = await _api.SearchAsync(customerId, GoogleAdsQueries.SearchTerms(date), ct);
        return GoogleAdsFactMapper.MergeDuplicates(rows.Select(row => GoogleAdsFactMapper.SearchTerm(row, currency)));
    }

    public Task<IReadOnlyList<AdChangeEventRow>> GetChangeEventsAsync(string accountExternalId, DateTimeOffset since, CancellationToken ct) =>
        throw new NotImplementedException("Task B7");

    private async Task<GoogleAdsCustomer> GetCustomerAsync(string customerId, CancellationToken ct)
    {
        if (_customers.TryGetValue(customerId, out var cached))
            return cached;

        var rows = await _api.SearchAsync(customerId, GoogleAdsQueries.Customer, ct);
        if (rows.Count != 1)
            throw new InvalidOperationException($"Google Ads customer {customerId} returned {rows.Count} customer rows.");

        var row = rows[0];
        if (GoogleAdsJson.Bool(row, "customer", "manager"))
            throw new InvalidOperationException(
                $"Google Ads customer {customerId} is a manager account. Set GoogleAds:CustomerId to the client " +
                "account and GoogleAds:LoginCustomerId to the manager.");

        var timeZoneId = GoogleAdsJson.RequiredString(row, "customer", "timeZone");
        var customer = new GoogleAdsCustomer(
            GoogleAdsJson.RequiredString(row, "customer", "id"),
            GoogleAdsJson.String(row, "customer", "descriptiveName") ?? customerId,
            GoogleAdsJson.RequiredString(row, "customer", "currencyCode"),
            timeZoneId,
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        _customers[customerId] = customer;
        return customer;
    }

    private string ConfiguredCustomerId() =>
        GoogleAdsIds.RequireNumericId(
            GoogleAdsIds.NormalizeCustomerId(_settings.CurrentValue.CustomerId), "GoogleAds:CustomerId");

    private string RequireConfiguredAccount(string accountExternalId)
    {
        var configured = ConfiguredCustomerId();
        return GoogleAdsIds.NormalizeCustomerId(accountExternalId) == configured
            ? configured
            : throw new ArgumentException(
                $"Google Ads account '{accountExternalId}' is not the configured account.", nameof(accountExternalId));
    }
}
