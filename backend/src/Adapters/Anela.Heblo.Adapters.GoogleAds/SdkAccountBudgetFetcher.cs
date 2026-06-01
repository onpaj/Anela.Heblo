using Google.Ads.GoogleAds;
using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds;

internal sealed class SdkAccountBudgetFetcher : IAccountBudgetFetcher, IDisposable
{
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<SdkAccountBudgetFetcher> _logger;
    private readonly Func<GoogleAdsConfig, GoogleAdsClient> _clientFactory;
    private readonly object _lock = new();
    private GoogleAdsClient? _cachedClient;
    private string? _cachedFingerprint;
    private IDisposable? _changeListener;

    public SdkAccountBudgetFetcher(
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<SdkAccountBudgetFetcher> logger)
        : this(settings, logger, cfg => new GoogleAdsClient(cfg))
    {
    }

    internal SdkAccountBudgetFetcher(
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<SdkAccountBudgetFetcher> logger,
        Func<GoogleAdsConfig, GoogleAdsClient> clientFactory)
    {
        _settings = settings;
        _logger = logger;
        _clientFactory = clientFactory;
        _changeListener = _settings.OnChange(_ => InvalidateCache());
    }

    private void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedClient = null;
            _cachedFingerprint = null;
        }

        _logger.LogDebug("GoogleAds: settings changed, client cache invalidated");
    }

    internal GoogleAdsClient GetOrCreateClient()
    {
        var s = _settings.CurrentValue;
        var fingerprint = $"{s.DeveloperToken}|{s.OAuth2ClientId}|{s.OAuth2ClientSecret}|{s.OAuth2RefreshToken}|{s.CustomerId}";

        lock (_lock)
        {
            if (_cachedClient is not null && _cachedFingerprint == fingerprint)
            {
                return _cachedClient;
            }

            var config = new GoogleAdsConfig
            {
                DeveloperToken = s.DeveloperToken,
                OAuth2ClientId = s.OAuth2ClientId,
                OAuth2ClientSecret = s.OAuth2ClientSecret,
                OAuth2RefreshToken = s.OAuth2RefreshToken,
                LoginCustomerId = s.CustomerId.Replace("-", ""),
            };

            _cachedClient = _clientFactory(config);
            _cachedFingerprint = fingerprint;

            _logger.LogInformation("GoogleAds: created new GoogleAdsClient");

            return _cachedClient;
        }
    }

    public async Task<IReadOnlyList<RawAccountBudget>> FetchAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var s = _settings.CurrentValue;
        var customerId = s.CustomerId.Replace("-", "");

        var client = GetOrCreateClient();
        var service = client.GetService(Services.V18.GoogleAdsService);

        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");

        var query = $"""
            SELECT
                account_budget.id,
                account_budget.name,
                account_budget.approved_start_date_time,
                account_budget.amount_served_micros,
                customer.currency_code
            FROM account_budget
            WHERE account_budget.status = 'APPROVED'
              AND account_budget.approved_start_date_time >= '{fromStr} 00:00:00'
              AND account_budget.approved_start_date_time <= '{toStr} 23:59:59'
            """;

        _logger.LogDebug("GoogleAds: executing GAQL query for account_budget from {From} to {To}", fromStr, toStr);

        var results = new List<RawAccountBudget>();

        using var stream = service.SearchStream(customerId, query);
        var responseStream = stream.GetResponseStream();

        while (await responseStream.MoveNextAsync(ct))
        {
            var response = responseStream.Current;

            foreach (var row in response.Results)
            {
                var ab = row.AccountBudget;
                if (ab is null)
                {
                    continue;
                }

                if (!DateTime.TryParse(ab.ApprovedStartDateTime, out var startDate))
                {
                    continue;
                }

                var currencyCode = row.Customer?.CurrencyCode ?? string.Empty;

                results.Add(new RawAccountBudget(
                    ab.Id.ToString(),
                    string.IsNullOrEmpty(ab.Name) ? null : ab.Name,
                    startDate.ToUniversalTime(),
                    ab.AmountServedMicros,
                    currencyCode));
            }
        }

        return results;
    }

    public void Dispose()
    {
        _changeListener?.Dispose();
    }
}
