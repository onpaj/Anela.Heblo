using System.Text.Json;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsTransactionSource : IMarketingTransactionSource
{
    private readonly IAccountBudgetFetcher _fetcher;
    private readonly ILogger<GoogleAdsTransactionSource> _logger;

    public string Platform => "GoogleAds";

    internal GoogleAdsTransactionSource(
        IAccountBudgetFetcher fetcher,
        ILogger<GoogleAdsTransactionSource> logger)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<MarketingTransaction>> GetTransactionsAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "GoogleAds: fetching account budgets from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            from,
            to);

        var rows = await _fetcher.FetchAsync(from, to, ct);

        var transactions = rows.Select(r => new MarketingTransaction
        {
            TransactionId = r.Id,
            Platform = Platform,
            Amount = r.AmountServedMicros / 1_000_000m,
            TransactionDate = r.StartDate,
            Currency = r.CurrencyCode,
            Description = r.Name ?? "Google Ads billing period",
            RawData = JsonSerializer.Serialize(r),
        }).ToList();

        _logger.LogInformation("GoogleAds: fetched {Count} account budgets", transactions.Count);

        return transactions;
    }
}
