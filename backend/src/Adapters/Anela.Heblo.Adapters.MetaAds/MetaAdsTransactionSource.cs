using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsTransactionSource : IMarketingTransactionSource
{
    private readonly HttpClient _httpClient;
    private readonly MetaAdsSettings _settings;
    private readonly ILogger<MetaAdsTransactionSource> _logger;
    private readonly ResiliencePipeline _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Platform => "MetaAds";

    public MetaAdsTransactionSource(
        HttpClient httpClient,
        IOptions<MetaAdsSettings> options,
        ILogger<MetaAdsTransactionSource> logger,
        ResiliencePipeline? pipeline = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeline = pipeline ?? BuildDefaultPipeline();
    }

    public async Task<List<MarketingTransaction>> GetTransactionsAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var results = new List<MarketingTransaction>();
        var url = BuildInitialUrl();

        _logger.LogInformation(
            "MetaAds: fetching transactions for account {AccountId} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            _settings.AccountId, from, to);

        try
        {
            while (url is not null)
            {
                var page = await FetchPageAsync(url, ct);

                foreach (var item in page.Data)
                {
                    var txDate = DateTimeOffset.FromUnixTimeSeconds(item.Time).UtcDateTime;

                    if (txDate < from || txDate > to)
                        continue;

                    results.Add(new MarketingTransaction
                    {
                        TransactionId = item.Id,
                        Platform = Platform,
                        Amount = item.Amount / 100m,
                        TransactionDate = txDate,
                        Currency = item.Currency,
                        Description = item.PaymentType,
                        RawData = JsonSerializer.Serialize(item, JsonOptions),
                    });
                }

                url = page.Paging?.Next;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "MetaAds: HTTP error fetching transactions for account {AccountId} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}. StatusCode={StatusCode}",
                _settings.AccountId, from, to, (int?)ex.StatusCode);
            throw;
        }

        _logger.LogInformation(
            "MetaAds: fetched {Count} transactions within date range", results.Count);

        return results;
    }

    private async Task<MetaTransactionsResponse> FetchPageAsync(string url, CancellationToken ct)
    {
        _logger.LogDebug("MetaAds: GET {Url}", RedactToken(url));

        return await _pipeline.ExecuteAsync(async innerCt =>
        {
            var response = await _httpClient.GetAsync(url, innerCt);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(innerCt);
            return JsonSerializer.Deserialize<MetaTransactionsResponse>(json, JsonOptions)
                   ?? throw new InvalidOperationException("MetaAds API returned null response body.");
        }, ct);
    }

    private string BuildInitialUrl() =>
        $"https://graph.facebook.com/{_settings.ApiVersion}/{_settings.AccountId}/transactions" +
        $"?fields=id,time,amount,currency,payment_type" +
        $"&access_token={_settings.AccessToken}";

    private static string RedactToken(string url)
    {
        var idx = url.IndexOf("access_token=", StringComparison.Ordinal);
        if (idx < 0) return url;
        var end = url.IndexOf('&', idx);
        return end < 0
            ? url[..idx] + "access_token=***"
            : url[..idx] + "access_token=***" + url[end..];
    }

    /// <summary>Production resilience pipeline: retry on HTTP 429 with exponential backoff.</summary>
    internal static ResiliencePipeline BuildDefaultPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            })
            .Build();

    /// <summary>Zero-delay pipeline for unit tests.</summary>
    internal static ResiliencePipeline BuildTestPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
            })
            .Build();

    // ── Internal deserialization models ──────────────────────────────────────

    private sealed class MetaTransactionsResponse
    {
        [JsonPropertyName("data")]
        public List<MetaTransactionItem> Data { get; set; } = [];

        [JsonPropertyName("paging")]
        public MetaPaging? Paging { get; set; }
    }

    private sealed class MetaTransactionItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("time")]
        public long Time { get; set; }

        /// <summary>Amount in cents (integer).</summary>
        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("payment_type")]
        public string PaymentType { get; set; } = string.Empty;
    }

    private sealed class MetaPaging
    {
        [JsonPropertyName("next")]
        public string? Next { get; set; }
    }
}
