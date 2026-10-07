using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>Google Ads REST transport: auth headers, paging, error parsing and transient retries.</summary>
internal sealed class GoogleAdsRestClient : IGoogleAdsApiClient
{
    internal const string HttpClientName = "GoogleAdsApi";
    internal static readonly Uri BaseAddress = new("https://googleads.googleapis.com/");
    private const int MaxPages = 100; // 10 000 rows per page; guards against an endless nextPageToken loop
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGoogleAdsAccessTokenProvider _tokens;
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly ILogger<GoogleAdsRestClient> _logger;
    private readonly ResiliencePipeline _searchPipeline;

    public GoogleAdsRestClient(
        IHttpClientFactory httpClientFactory,
        IGoogleAdsAccessTokenProvider tokens,
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<GoogleAdsRestClient> logger)
        : this(httpClientFactory, tokens, settings, logger, DefaultRetryDelay)
    {
    }

    internal GoogleAdsRestClient(
        IHttpClientFactory httpClientFactory,
        IGoogleAdsAccessTokenProvider tokens,
        IOptionsMonitor<GoogleAdsSettings> settings,
        ILogger<GoogleAdsRestClient> logger,
        TimeSpan retryDelay)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _settings = settings;
        _logger = logger;
        _searchPipeline = BuildSearchPipeline(retryDelay);
    }

    public async Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct)
    {
        GoogleAdsIds.RequireNumericId(customerId, nameof(customerId));
        var rows = new List<JsonElement>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var body = new JsonObject { ["query"] = query.Gaql };
            if (pageToken is not null)
                body["pageToken"] = pageToken;

            using var document = await _searchPipeline.ExecuteAsync(
                async innerCt => await PostAsync($"customers/{customerId}/googleAds:search", body, query.Name, innerCt), ct);
            rows.AddRange(ReadResults(document.RootElement));

            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
            if (string.IsNullOrEmpty(pageToken))
            {
                _logger.LogDebug("GoogleAds: query {Query} returned {Rows} rows", query.Name, rows.Count);
                return rows;
            }
        }

        throw new GoogleAdsApiException(
            $"Google Ads query '{query.Name}' exceeded {MaxPages} pages.", null, "client.too_many_pages", null, isTransient: false);
    }

    internal static GoogleAdsApiException ToException(HttpStatusCode status, string body, string operation)
    {
        var error = GoogleAdsErrorParser.Parse(body);
        var code = (int)status;
        var isTransient = status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500;
        return new GoogleAdsApiException(
            $"Google Ads {operation} failed: HTTP {code} {error.ErrorCode ?? "(no error code)"}: {error.Message}",
            status, error.ErrorCode, error.RequestId, isTransient);
    }

    /// <summary>
    /// Only failures worth repeating: Google's transient statuses, transport faults without an HTTP
    /// status, and HttpClient timeouts. TaskCanceledException is an OperationCanceledException, so the
    /// caller's own cancellation is told apart by its token, not by the exception type.
    /// </summary>
    internal static bool IsTransient(Exception? exception, CancellationToken ct) => exception switch
    {
        GoogleAdsApiException api => api.IsTransient,
        HttpRequestException http => http.StatusCode is null
                                     || http.StatusCode == HttpStatusCode.RequestTimeout
                                     || (int)http.StatusCode >= 500,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };

    private static ResiliencePipeline BuildSearchPipeline(TimeSpan delay) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = delay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = delay > TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(
                    IsTransient(args.Outcome.Exception, args.Context.CancellationToken)),
            })
            .Build();

    private static IEnumerable<JsonElement> ReadResults(JsonElement root) =>
        root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().Select(r => r.Clone()).ToList()
            : Enumerable.Empty<JsonElement>();

    /// <summary>
    /// A 401 means the cached access token was revoked before it expired; it is dropped and the
    /// request resent once with a fresh one. A second 401 is surfaced as a permanent error.
    /// </summary>
    private async Task<JsonDocument> PostAsync(string path, JsonObject body, string operation, CancellationToken ct)
    {
        var (response, accessToken) = await SendAsync(path, body, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _tokens.Invalidate(accessToken);
            (response, _) = await SendAsync(path, body, ct);
        }

        using (response)
            return await ReadAsync(response, operation, ct);
    }

    private async Task<(HttpResponseMessage Response, string AccessToken)> SendAsync(
        string path, JsonObject body, CancellationToken ct)
    {
        var accessToken = await _tokens.GetAccessTokenAsync(ct);
        using var request = CreateRequest(path, body, accessToken);
        var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
        return (response, accessToken);
    }

    private async Task<JsonDocument> ReadAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
            return JsonDocument.Parse(text);

        var error = ToException(response.StatusCode, text, operation);
        _logger.LogWarning(
            "GoogleAds: {Operation} failed with HTTP {Status} {ErrorCode} (requestId {RequestId})",
            operation, (int)response.StatusCode, error.ErrorCode, error.RequestId);
        throw error;
    }

    private HttpRequestMessage CreateRequest(string path, JsonObject body, string accessToken)
    {
        var settings = _settings.CurrentValue;
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, $"{settings.ApiVersion}/{path}"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var loginCustomerId = GoogleAdsIds.NormalizeCustomerId(settings.LoginCustomerId);
        if (loginCustomerId.Length > 0)
            request.Headers.TryAddWithoutValidation("login-customer-id", loginCustomerId);
        return request;
    }
}
