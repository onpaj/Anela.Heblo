using System.Net;
using System.Text.Json;
using Anela.Heblo.Application.Shared.WebSearch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.WebSearch;

public class SerpApiWebSearchClient : IWebSearchClient
{
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ex =>
            ex.StatusCode == HttpStatusCode.TooManyRequests ||
            ex.StatusCode == HttpStatusCode.ServiceUnavailable ||
            ex.StatusCode is null) // null = network-level error (no HTTP response)
        })
        .Build();

    private readonly WebSearchAdapterOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SerpApiWebSearchClient> _logger;

    public SerpApiWebSearchClient(
        IOptions<WebSearchAdapterOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SerpApiWebSearchClient> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<WebSearchResult> SearchAsync(string query, WebSearchOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            throw new InvalidOperationException("WebSearch:ApiKey is not configured.");

        var client = _httpClientFactory.CreateClient("SerpApi");
        var locale = options.Locale ?? _options.DefaultLocale;
        var geo = options.Geo ?? _options.DefaultGeo;
        var num = options.Top;

        // NOTE: SerpAPI requires the key as a query param. Do NOT add a URL-logging DelegatingHandler to this client.
        var url = $"{_options.Endpoint}?q={Uri.EscapeDataString(query)}&hl={locale}&gl={geo}&num={num}&api_key={_options.ApiKey}";

        _logger.LogDebug("Web search: {Query} (locale={Locale}, geo={Geo}, top={Top})", query, locale, geo, num);

        var response = await Pipeline.ExecuteAsync(
            async token => await client.GetAsync(url, token),
            ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseSerpApiResponse(query, json);
    }

    private WebSearchResult ParseSerpApiResponse(string query, string json)
    {
        var result = new WebSearchResult { Query = query };
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("organic_results", out var organic))
                return result;

            var hits = new List<WebSearchHit>();
            foreach (var item in organic.EnumerateArray())
            {
                hits.Add(new WebSearchHit
                {
                    Title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Url = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "",
                    Snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : ""
                });
            }
            result.Hits = hits;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse SerpAPI response for query {Query}", query);
        }
        return result;
    }
}
