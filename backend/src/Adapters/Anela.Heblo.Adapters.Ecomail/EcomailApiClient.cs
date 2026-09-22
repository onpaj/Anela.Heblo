using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Ecomail;

public class EcomailApiClient : IEcomailApiClient
{
    public const string HttpClientName = "Ecomail";

    /// <summary>Ecomail rejects per_page above 50 with a 422.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// Generous headroom over the ~5 pages this account currently returns. Ecomail's paging is
    /// driven entirely by a short-page check (see below), so if it ever ignored page/per_page the
    /// loop would run forever against a live account with no sandbox, every 6 hours.
    /// </summary>
    private const int MaxCampaignPages = 200;

    private static readonly ResiliencePipeline DefaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<EcomailThrottledException>(),
            // Honour Ecomail's own Retry-After when it threw one; fall back to the
            // exponential backoff above (returning null tells Polly to use its default).
            DelayGenerator = static args =>
            {
                if (args.Outcome.Exception is EcomailThrottledException { RetryAfter: var retryAfter })
                    return new ValueTask<TimeSpan?>(retryAfter);
                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            },
        })
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new EcomailNullableDateTimeConverter() },
    };

    private readonly EcomailOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EcomailApiClient> _logger;
    private readonly ResiliencePipeline _pipeline;

    public EcomailApiClient(
        IOptions<EcomailOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<EcomailApiClient> logger,
        ResiliencePipeline? pipeline = null)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pipeline = pipeline ?? DefaultPipeline;
    }

    public async Task<IReadOnlyList<EcomailCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<EcomailCampaignDto>();

        for (var page = 1; page <= MaxCampaignPages; page++)
        {
            var url = $"/campaigns?per_page={PageSize}&page={page}";
            var batch = await GetAsync<List<EcomailCampaignDto>>(url, cancellationToken) ?? new List<EcomailCampaignDto>();

            all.AddRange(batch);

            // A short page means the last page. Ecomail returns a bare array with no total.
            if (batch.Count < PageSize)
            {
                return all;
            }
        }

        _logger.LogWarning(
            "Ecomail campaigns paging hit the {MaxPages}-page cap without a short page; stopping with {Count} campaigns fetched so far",
            MaxCampaignPages, all.Count);
        return all;
    }

    public async Task<EcomailStatsDto?> GetCampaignStatsAsync(int campaignId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<StatsEnvelope>($"/campaigns/{campaignId}/stats", cancellationToken);
        return envelope?.Stats;
    }

    public async Task<IReadOnlyList<EcomailPipelineDto>> GetPipelinesAsync(CancellationToken cancellationToken = default)
        => await GetAsync<List<EcomailPipelineDto>>("/pipelines", cancellationToken) ?? new List<EcomailPipelineDto>();

    public async Task<EcomailStatsDto?> GetPipelineStatsAsync(int pipelineId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<StatsEnvelope>($"/pipelines/{pipelineId}/stats", cancellationToken);
        return envelope?.Stats;
    }

    public async Task<int> GetPipelineEventCountAsync(
        int pipelineId, string eventName, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        // per_page=1 because only `total` is read — never page through subscribers.
        var url = $"/pipelines/{pipelineId}/stats-detail" +
                  $"?event={Uri.EscapeDataString(eventName)}" +
                  $"&from_date={fromDate:yyyy-MM-dd}&to_date={toDate:yyyy-MM-dd}&per_page=1";

        var detail = await GetAsync<StatsDetailEnvelope>(url, cancellationToken);
        return detail?.Total ?? 0;
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        return await _pipeline.ExecuteAsync(async ct =>
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("key", _options.ApiKey);

            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _logger.LogWarning("Ecomail throttled {Url}; retrying after {RetryAfter}", url, retryAfter);
                throw new EcomailThrottledException(retryAfter);
            }

            // A deleted campaign returns 404 and must not fail the whole sync. A 403 is left to
            // EnsureSuccessStatusCode below on purpose: it means a permission/scope problem, not a
            // missing resource, and this API key is already known to 403 on some endpoints
            // (CLUSTER-B-FINDINGS.md). Swallowing it the same way as 404 would let a scope change
            // on Ecomail's side turn into a silently empty, "successful" run forever.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Ecomail returned {Status} for {Url}; skipping", response.StatusCode, url);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }, cancellationToken);
    }

    private sealed class StatsEnvelope
    {
        public EcomailStatsDto? Stats { get; set; }
    }

    private sealed class StatsDetailEnvelope
    {
        public int? Total { get; set; }
    }
}

public sealed class EcomailThrottledException : Exception
{
    public EcomailThrottledException(TimeSpan retryAfter)
        : base($"Ecomail throttled the request; retry after {retryAfter}.")
        => RetryAfter = retryAfter;

    public TimeSpan RetryAfter { get; }
}
