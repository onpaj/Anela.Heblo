using Google.Ads.GoogleAds;
using Google.Ads.GoogleAds.Config;
using Google.Ads.GoogleAds.Lib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds;

internal sealed class SdkAccountBudgetFetcher : IAccountBudgetFetcher
{
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<SdkAccountBudgetFetcher> _logger;

    public SdkAccountBudgetFetcher(
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<SdkAccountBudgetFetcher> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RawAccountBudget>> FetchAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var s = _settings.CurrentValue;
        var customerId = s.CustomerId.Replace("-", "");

        var config = new GoogleAdsConfig
        {
            DeveloperToken = s.DeveloperToken,
            OAuth2ClientId = s.OAuth2ClientId,
            OAuth2ClientSecret = s.OAuth2ClientSecret,
            OAuth2RefreshToken = s.OAuth2RefreshToken,
            LoginCustomerId = customerId,
        };

        var client = new GoogleAdsClient(config);
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
}
