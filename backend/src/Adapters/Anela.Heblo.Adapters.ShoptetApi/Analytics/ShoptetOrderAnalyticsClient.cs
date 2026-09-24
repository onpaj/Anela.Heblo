using System.Globalization;
using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public sealed class ShoptetOrderAnalyticsClient : IShoptetOrderAnalyticsClient
{
    /// <summary>Shoptet caps itemsPerPage at 50 on /api/orders; asking for 100 silently returns 50.</summary>
    public const int MaxItemsPerPage = 50;

    /// <summary>
    /// Shoptet rejects a date-time filter that is not full ISO 8601 with an explicit offset, and the
    /// "+" must survive URL encoding. "2026-09-21" or "2026-09-21T00:00:00" both return 400.
    /// </summary>
    private const string ShoptetDateTimeFormat = "yyyy-MM-ddTHH:mm:sszzz";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ShoptetApiThrottle _throttle;
    private readonly ShoptetOrdersSyncOptions _options;
    private readonly ILogger<ShoptetOrderAnalyticsClient> _logger;

    public ShoptetOrderAnalyticsClient(
        HttpClient http,
        ShoptetApiThrottle throttle,
        IOptions<ShoptetOrdersSyncOptions> options,
        ILogger<ShoptetOrderAnalyticsClient> logger)
    {
        _http = http;
        _throttle = throttle;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ShoptetOrderCodeListData> ListCodesByCreationTimeAsync(
        DateTimeOffset createdFrom, DateTimeOffset createdTo, int page, CancellationToken ct = default)
    {
        var url = $"/api/orders?itemsPerPage={MaxItemsPerPage}&page={page}"
                  + $"&creationTimeFrom={Encode(createdFrom)}&creationTimeTo={Encode(createdTo)}";
        return GetListAsync(url, ct);
    }

    public Task<ShoptetOrderCodeListData> ListCodesByChangeTimeAsync(
        DateTimeOffset changedFrom, int page, CancellationToken ct = default)
    {
        var url = $"/api/orders?itemsPerPage={MaxItemsPerPage}&page={page}"
                  + $"&changeTimeFrom={Encode(changedFrom)}";
        return GetListAsync(url, ct);
    }

    public async Task<ShoptetOrderChangeListData> ListChangesAsync(
        DateTimeOffset changedFrom, int page, int itemsPerPage, CancellationToken ct = default)
    {
        var url = $"/api/orders/changes?from={Encode(changedFrom)}&page={page}&itemsPerPage={itemsPerPage}";
        var json = await SendAsync(url, allowNotFound: false, ct);

        var parsed = JsonSerializer.Deserialize<ShoptetOrderChangeListResponse>(json!, JsonOptions);
        return parsed?.Data
               ?? throw new ShoptetOrderSyncException(
                   $"Shoptet returned an unrecognised change-log envelope for {url}");
    }

    public async Task<ShoptetOrderDetailDto?> GetOrderAsync(string code, CancellationToken ct = default)
        => (await GetOrderWithRawAsync(code, ct)).Order;

    public async Task<(ShoptetOrderDetailDto? Order, string RawJson)> GetOrderWithRawAsync(
        string code, CancellationToken ct = default)
    {
        // The only endpoint where 404 is an expected, benign answer: the order was deleted between
        // the listing and this call. Every other endpoint treats 404 as a hard failure.
        var json = await SendAsync($"/api/orders/{Uri.EscapeDataString(code)}", allowNotFound: true, ct);
        if (json == null)
            return (null, "{}");

        var parsed = JsonSerializer.Deserialize<ShoptetOrderDetailResponse>(json, JsonOptions);
        return (parsed?.Data?.Order, json);
    }

    private async Task<ShoptetOrderCodeListData> GetListAsync(string url, CancellationToken ct)
    {
        var json = await SendAsync(url, allowNotFound: false, ct);

        // An unrecognised envelope must never degrade to "no orders": the callers advance their
        // cursor or watermark past whatever they were told, so an empty page is indistinguishable
        // from a window that genuinely holds nothing and silently skips real orders for ever.
        var parsed = JsonSerializer.Deserialize<ShoptetOrderCodeListResponse>(json!, JsonOptions);
        return parsed?.Data
               ?? throw new ShoptetOrderSyncException(
                   $"Shoptet returned an unrecognised order-list envelope for {url}");
    }

    /// <summary>
    /// Returns the response body. Returns null only when <paramref name="allowNotFound"/> is set
    /// and Shoptet answered 404; otherwise a 404 throws like any other failure status.
    /// </summary>
    private async Task<string?> SendAsync(string url, bool allowNotFound, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await _throttle.WaitAsync(ct);

            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, ct);
            }
            catch (Exception ex) when (IsTransient(ex, ct) && attempt < _options.MaxRetryAttempts)
            {
                // A seven-hour backfill will hit the occasional dropped connection or per-request
                // timeout. Note the filter is on the caller's token, not the exception type: an
                // HttpClient timeout surfaces as TaskCanceledException, which IS an
                // OperationCanceledException, so `ex is not OperationCanceledException` would let
                // real timeouts through while swallowing genuine cancellation.
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex,
                    "ShoptetOrdersSync.TransientRequestFailure url={Url} attempt={Attempt}",
                    url, attempt + 1);
                await Task.Delay(backoff, ct);
                continue;
            }

            using (response)
            {
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                var isRetryableStatus =
                    response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;

                if (isRetryableStatus && attempt < _options.MaxRetryAttempts)
                {
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        // Push the shared pacer out too, so every caller behind it backs off.
                        _throttle.Penalize(backoff);
                        _logger.LogWarning(
                            "ShoptetOrdersSync.RateLimited url={Url} attempt={Attempt} backoffSeconds={Backoff}",
                            url, attempt + 1, backoff.TotalSeconds);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ShoptetOrdersSync.ServerError url={Url} status={Status} attempt={Attempt}",
                            url, (int)response.StatusCode, attempt + 1);
                    }

                    await Task.Delay(backoff, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
        }
    }

    /// <summary>
    /// True for a network-level failure worth retrying. Genuine cancellation — the operator
    /// pressing Ctrl+C, or the job's overall timeout — is never retried.
    /// </summary>
    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException;

    private static string Encode(DateTimeOffset value)
        => Uri.EscapeDataString(value.ToString(ShoptetDateTimeFormat, CultureInfo.InvariantCulture));
}
